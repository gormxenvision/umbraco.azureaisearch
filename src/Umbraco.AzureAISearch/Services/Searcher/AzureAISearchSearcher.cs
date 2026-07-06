using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Umbraco.AzureAISearch.Constants;
using Umbraco.AzureAISearch.Services.Factory;
using Umbraco.AzureAISearch.Services.IndexAliasResolver;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Models.Searching.Sorting;
using Umbraco.Cms.Search.Core.Services;

namespace Umbraco.AzureAISearch.Services.Searcher;

public interface IAzureAISearchSearcher : ISearcher
{
    Task<SearchResult> SearchAsync(
        string indexAlias,
        string? query = null,
        IEnumerable<Filter>? filters = null,
        IEnumerable<Facet>? facets = null,
        IEnumerable<Sorter>? sorters = null,
        string? culture = null,
        string? segment = null,
        AccessContext? accessContext = null,
        int skip = 0,
        int take = 10,
        int maxSuggestions = 0,
        SearchQueryType queryType = SearchQueryType.Simple,
        SearchMode searchMode = SearchMode.All);
}

internal sealed class AzureAISearchSearcher(
    IAzureSearchClientFactory clientFactory,
    IIndexAliasResolver aliasResolver,
    IServerRoleAccessor serverRoleAccessor,
    ILogger<AzureAISearchSearcher> logger)
    : UmbracoAzureServiceBase(serverRoleAccessor), IAzureAISearchSearcher
{
    public Task<SearchResult> SearchAsync(
        string indexAlias,
        string? query = null,
        IEnumerable<Filter>? filters = null,
        IEnumerable<Facet>? facets = null,
        IEnumerable<Sorter>? sorters = null,
        string? culture = null,
        string? segment = null,
        AccessContext? accessContext = null,
        int skip = 0,
        int take = 10,
        int maxSuggestions = 0,
        SearchQueryType queryType = SearchQueryType.Simple,
        SearchMode searchMode = SearchMode.All)
        => SearchCoreAsync(indexAlias, query, filters, facets, sorters,
            culture, segment, accessContext, skip, take, queryType, searchMode);

    // Explicit ISearcher implementation
    Task<SearchResult> ISearcher.SearchAsync(
        string indexAlias,
        string? query,
        IEnumerable<Filter>? filters,
        IEnumerable<Facet>? facets,
        IEnumerable<Sorter>? sorters,
        string? culture,
        string? segment,
        AccessContext? accessContext,
        int skip,
        int take,
        int maxSuggestions)
        => SearchCoreAsync(indexAlias, query, filters, facets, sorters,
            culture, segment, accessContext, skip, take, SearchQueryType.Simple, SearchMode.All);

    private async Task<SearchResult> SearchCoreAsync(
        string indexAlias,
        string? query,
        IEnumerable<Filter>? filters,
        IEnumerable<Facet>? facets,
        IEnumerable<Sorter>? sorters,
        string? culture,
        string? segment,
        AccessContext? accessContext,
        int skip,
        int take,
        SearchQueryType queryType,
        SearchMode searchMode)
    {
        var indexName = aliasResolver.Resolve(indexAlias);
        var searchClient = clientFactory.GetSearchClient(indexName);

        var searchOptions = new SearchOptions
        {
            Size = take,
            Skip = skip,
            IncludeTotalCount = true,
            QueryType = queryType,
            SearchMode = searchMode,
            ScoringProfile = IndexConstants.ScoringProfiles.RelevanceBoost,
        };

        // Build search text
        var hasFilters = filters?.Any() == true;
        var hasFacets = facets?.Any() == true;
        var hasSorters = sorters?.Any() == true;
        if (string.IsNullOrWhiteSpace(query) && !hasFilters && !hasFacets && !hasSorters)
        {
            return new SearchResult(0, [], []);
        }

        var searchText = string.IsNullOrWhiteSpace(query)
            ? "*"
            : query.Contains(' ') ? query : $"{query}*";

        // Build filter clauses
        var filterClauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(culture))
        {
            filterClauses.Add(
                $"({IndexConstants.FieldNames.Culture} eq '{culture}' or {IndexConstants.FieldNames.Culture} eq '{IndexConstants.Variation.InvariantCulture}')");
        }
        else
        {
            filterClauses.Add(
                $"{IndexConstants.FieldNames.Culture} eq '{IndexConstants.Variation.InvariantCulture}'");
        }

        if (!string.IsNullOrWhiteSpace(segment))
        {
            filterClauses.Add(
                $"({IndexConstants.FieldNames.Segment} eq '{segment}' or {IndexConstants.FieldNames.Segment} eq '{IndexConstants.Variation.DefaultSegment}')");
        }
        else
        {
            filterClauses.Add(
                $"{IndexConstants.FieldNames.Segment} eq '{IndexConstants.Variation.DefaultSegment}'");
        }

        if (filters is not null)
        {
            foreach (var filter in filters)
            {
                var filterStr = BuildFilterClause(filter);
                if (!string.IsNullOrEmpty(filterStr))
                    filterClauses.Add(filterStr);
            }
        }

        if (filterClauses.Count > 0)
        {
            searchOptions.Filter = string.Join(" and ", filterClauses);
        }

        // Default sort by score descending
        searchOptions.OrderBy.Add("search.score() desc");

        try
        {
            var response = await searchClient.SearchAsync<SearchDocument>(searchText, searchOptions);
            var documents = new List<Document>();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                if (result.Document.TryGetValue(IndexConstants.FieldNames.Key, out var keyValue) &&
                    Guid.TryParse(keyValue?.ToString(), out var key) &&
                    result.Document.TryGetValue(IndexConstants.FieldNames.ObjectType, out var objectTypeValue) &&
                    Enum.TryParse<UmbracoObjectTypes>(objectTypeValue?.ToString(), out var objType))
                {
                    documents.Add(new Document(key, objType));
                }
            }

            return new SearchResult(response.Value.TotalCount ?? 0, documents.ToArray(), []);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search failed for query '{Query}' on index {IndexName}", query, indexName);
            throw;
        }
    }

    private static string BuildFilterClause(Filter filter)
    {
        return filter switch
        {
            KeywordFilter kf => BuildKeywordExactFilter(kf),
            IntegerExactFilter ief => BuildIntegerExactFilter(ief),
            DecimalExactFilter def => BuildDecimalExactFilter(def),
            DateTimeOffsetExactFilter dtf => BuildDateTimeExactFilter(dtf),
            IntegerRangeFilter irf => BuildIntegerRangeFilter(irf),
            DecimalRangeFilter drf => BuildDecimalRangeFilter(drf),
            DateTimeOffsetRangeFilter dtrf => BuildDateTimeRangeFilter(dtrf),
            TextFilter tf => BuildTextFilter(tf),
            _ => string.Empty
        };
    }

    private static string BuildKeywordExactFilter(KeywordFilter filter)
    {
        var fieldName = $"{filter.FieldName}{IndexConstants.FieldTypePostfix.Keywords}";
        var valueList = filter.Values.ToArray();
        if (valueList.Length == 0) return string.Empty;

        var condition = string.Join(" or ", valueList.Select(v => $"k eq '{EscapeOData(v)}'"));
        var clause = $"{fieldName}/any(k: {condition})";
        return filter.Negate ? $"not ({clause})" : clause;
    }

    private static string BuildIntegerExactFilter(IntegerExactFilter filter)
    {
        var fieldName = $"{filter.FieldName}{IndexConstants.FieldTypePostfix.Integers}";
        var values = filter.Values;
        if (values.Length == 0) return string.Empty;

        var condition = string.Join(" or ", values.Select(v => $"i eq {v}"));
        var clause = $"{fieldName}/any(i: {condition})";
        return filter.Negate ? $"not ({clause})" : clause;
    }

    private static string BuildDecimalExactFilter(DecimalExactFilter filter)
    {
        var fieldName = $"{filter.FieldName}{IndexConstants.FieldTypePostfix.Decimals}";
        var values = filter.Values;
        if (values.Length == 0) return string.Empty;

        var condition = string.Join(" or ", values.Select(v => $"d eq {v}"));
        var clause = $"{fieldName}/any(d: {condition})";
        return filter.Negate ? $"not ({clause})" : clause;
    }

    private static string BuildDateTimeExactFilter(DateTimeOffsetExactFilter filter)
    {
        var fieldName = $"{filter.FieldName}{IndexConstants.FieldTypePostfix.DateTimeOffsets}";
        var values = filter.Values;
        if (values.Length == 0) return string.Empty;

        var condition = string.Join(" or ", values.Select(v => $"d eq {v:O}"));
        var clause = $"{fieldName}/any(d: {condition})";
        return filter.Negate ? $"not ({clause})" : clause;
    }

    private static string BuildIntegerRangeFilter(IntegerRangeFilter filter)
    {
        var fieldName = $"{filter.FieldName}{IndexConstants.FieldTypePostfix.Integers}{IndexConstants.FieldTypePostfix.Sortable}";
        var ranges = filter.Ranges;
        if (ranges.Length == 0) return string.Empty;

        var conditions = ranges.Select(r =>
        {
            var parts = new List<string>();
            if (r.MinValue.HasValue) parts.Add($"{fieldName} ge {r.MinValue.Value}");
            if (r.MaxValue.HasValue) parts.Add($"{fieldName} le {r.MaxValue.Value}");
            return parts.Count > 0 ? $"({string.Join(" and ", parts)})" : string.Empty;
        }).Where(s => s.Length > 0);

        var clause = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({clause})" : clause;
    }

    private static string BuildDecimalRangeFilter(DecimalRangeFilter filter)
    {
        var fieldName = $"{filter.FieldName}{IndexConstants.FieldTypePostfix.Decimals}{IndexConstants.FieldTypePostfix.Sortable}";
        var ranges = filter.Ranges;
        if (ranges.Length == 0) return string.Empty;

        var conditions = ranges.Select(r =>
        {
            var parts = new List<string>();
            if (r.MinValue.HasValue) parts.Add($"{fieldName} ge {r.MinValue.Value}");
            if (r.MaxValue.HasValue) parts.Add($"{fieldName} le {r.MaxValue.Value}");
            return parts.Count > 0 ? $"({string.Join(" and ", parts)})" : string.Empty;
        }).Where(s => s.Length > 0);

        var clause = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({clause})" : clause;
    }

    private static string BuildDateTimeRangeFilter(DateTimeOffsetRangeFilter filter)
    {
        var fieldName = $"{filter.FieldName}{IndexConstants.FieldTypePostfix.DateTimeOffsets}{IndexConstants.FieldTypePostfix.Sortable}";
        var ranges = filter.Ranges;
        if (ranges.Length == 0) return string.Empty;

        var conditions = ranges.Select(r =>
        {
            var parts = new List<string>();
            if (r.MinValue.HasValue) parts.Add($"{fieldName} ge {r.MinValue.Value:O}");
            if (r.MaxValue.HasValue) parts.Add($"{fieldName} le {r.MaxValue.Value:O}");
            return parts.Count > 0 ? $"({string.Join(" and ", parts)})" : string.Empty;
        }).Where(s => s.Length > 0);

        var clause = string.Join(" or ", conditions);
        return filter.Negate ? $"not ({clause})" : clause;
    }

    private static string BuildTextFilter(TextFilter filter)
    {
        var fieldName = $"{filter.FieldName}{IndexConstants.FieldTypePostfix.Texts}";
        var values = filter.Values;
        if (values.Length == 0) return string.Empty;

        var condition = string.Join(" or ", values.Select(v => $"search.ismatch('{EscapeOData(v)}', '{fieldName}')"));
        return filter.Negate ? $"not ({condition})" : condition;
    }

    private static string EscapeOData(string value) => value.Replace("'", "''");
}
