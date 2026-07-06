using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Umbraco.AzureAISearch.Constants;
using Umbraco.AzureAISearch.Extensions;
using Umbraco.AzureAISearch.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Search.Core.Models.Indexing;

namespace Umbraco.AzureAISearch.Services.Indexer;

internal sealed class MappingResult
{
    public required List<SearchDocument> Documents { get; init; }
    public required List<IndexFieldMapping> FieldMappings { get; init; }
}

internal sealed class DocumentMapper
{
    private readonly string? _baseUrl;
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IPublishedUrlProvider _publishedUrlProvider;

    public DocumentMapper(
        IOptions<AzureAISearchOptions> options,
        IUmbracoContextFactory umbracoContextFactory,
        IPublishedUrlProvider publishedUrlProvider)
    {
        _baseUrl = string.IsNullOrWhiteSpace(options.Value.BaseUrl)
            ? null
            : options.Value.BaseUrl.TrimEnd('/');
        _umbracoContextFactory = umbracoContextFactory;
        _publishedUrlProvider = publishedUrlProvider;
    }

    public MappingResult MapToDocuments(
        Guid contentKey,
        UmbracoObjectTypes objectType,
        IEnumerable<Variation> variations,
        IEnumerable<IndexField> fields,
        ContentProtection? protection)
    {
        var fieldsByFieldName = fields.GroupBy(f => f.FieldName).ToList();
        var documents = new List<SearchDocument>();
        var allFieldMappings = new List<IndexFieldMapping>();

        var resolvedUrls = ResolveContentUrls(contentKey);

        foreach (var variation in variations)
        {
            var (document, variationMappings) = MapVariationToDocument(
                contentKey, objectType, variation, fieldsByFieldName, protection, resolvedUrls);

            documents.Add(document);
            allFieldMappings.AddRange(variationMappings);
        }

        // Deduplicate field mappings by field name
        var uniqueMappings = allFieldMappings
            .GroupBy(m => m.FieldName)
            .Select(g => g.First())
            .ToList();

        return new MappingResult { Documents = documents, FieldMappings = uniqueMappings };
    }

    private (SearchDocument Document, List<IndexFieldMapping> FieldMappings) MapVariationToDocument(
        Guid contentKey,
        UmbracoObjectTypes objectType,
        Variation variation,
        List<IGrouping<string, IndexField>> fieldsByFieldName,
        ContentProtection? protection,
        Dictionary<string, string> resolvedUrls)
    {
        var culture = variation.Culture ?? IndexConstants.Variation.InvariantCulture;
        var segment = variation.Segment ?? IndexConstants.Variation.DefaultSegment;
        var documentId = $"{contentKey:D}_{culture}_{segment}";

        var variationFields = GetVariationFields(fieldsByFieldName, variation);

        // Strip HTML from all text values
        variationFields = variationFields
            .Select(field => field with
            {
                Value = field.Value with
                {
                    Texts = StripHtml(field.Value.Texts),
                    TextsR1 = StripHtml(field.Value.TextsR1),
                    TextsR2 = StripHtml(field.Value.TextsR2),
                    TextsR3 = StripHtml(field.Value.TextsR3),
                }
            })
            .ToArray();

        // Aggregate per-tier text collections
        var textsR1 = variationFields.SelectMany(f => f.Value.TextsR1 ?? []).ToArray();
        var textsR2 = variationFields.SelectMany(f => f.Value.TextsR2 ?? []).ToArray();
        var textsR3 = variationFields.SelectMany(f => f.Value.TextsR3 ?? []).ToArray();
        var plainTexts = variationFields.SelectMany(f => f.Value.Texts ?? []).ToArray();

        // Build Foundry 'content' field — include all text tiers + non-system keywords (deduplicated)
        var contentParts = new List<string>();
        contentParts.AddRange(textsR1);
        contentParts.AddRange(textsR2);
        contentParts.AddRange(textsR3);
        contentParts.AddRange(plainTexts);

        // Include non-system keywords in content for Foundry search
        foreach (var field in variationFields)
        {
            if (field.Value.Keywords is { } kw && kw.Any()
                && !IsSystemKeywordField(field.FieldName))
            {
                contentParts.AddRange(kw.Where(k => !IsStructuralValue(k)));
            }
        }

        var content = string.Join(" ", contentParts.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());

        // Title: first R1 text
        var title = textsR1.FirstOrDefault() ?? plainTexts.FirstOrDefault() ?? string.Empty;

        // URL resolution
        string? route = null;
        var urlField = variationFields.FirstOrDefault(f => f.FieldName == "url");
        if (urlField?.Value.Keywords?.FirstOrDefault() is { } urlRoute)
            route = urlRoute;

        var url = (variation.Culture is not null && resolvedUrls.TryGetValue(variation.Culture, out var cultUrl) ? cultUrl : null)
                  ?? (resolvedUrls.TryGetValue(string.Empty, out var invUrl) ? invUrl : null)
                  ?? BuildUrl(route);

        // Access keys
        var accessKeys = protection?.AccessIds?.Any() is true
            ? protection.AccessIds.Select(g => g.ToString("D")).ToArray()
            : new[] { Guid.Empty.ToString("D") };

        // PathIds for cascade delete
        var pathIdsField = variationFields.FirstOrDefault(f =>
            string.Equals(f.FieldName, "pathIds", StringComparison.OrdinalIgnoreCase));
        var pathIds = pathIdsField?.Value.Keywords ?? [];

        // Create per-property field mappings
        var fieldMappings = CreateFieldMappings(variationFields).ToList();

        // Build document with fixed Foundry fields + aggregate fields
        var doc = new SearchDocument
        {
            [IndexConstants.FieldNames.Id] = documentId,
            [IndexConstants.FieldNames.Key] = contentKey.ToString("D"),
            [IndexConstants.FieldNames.ObjectType] = objectType.ToString(),
            [IndexConstants.FieldNames.Culture] = culture,
            [IndexConstants.FieldNames.Segment] = segment,
            [IndexConstants.FieldNames.Title] = title,
            [IndexConstants.FieldNames.Content] = content,
            [IndexConstants.FieldNames.ContentR1] = textsR1,
            [IndexConstants.FieldNames.ContentR2] = textsR2,
            [IndexConstants.FieldNames.ContentR3] = textsR3,
            [IndexConstants.FieldNames.Url] = url,
            [IndexConstants.FieldNames.AccessKeys] = accessKeys,
            [$"{IndexConstants.FieldNames.PathIds}{IndexConstants.FieldTypePostfix.Keywords}"] = pathIds,
        };

        // Add per-property field values to document
        foreach (var mapping in fieldMappings)
        {
            doc[mapping.FieldName] = mapping.IsCollection
                ? mapping.Values
                : mapping.Values.FirstOrDefault();
        }

        return (doc, fieldMappings);
    }

    private static IEnumerable<IndexFieldMapping> CreateFieldMappings(IndexField[] variationFields)
    {
        foreach (var field in variationFields)
        {
            // Texts (aggregates all text relevance levels)
            var allTexts = (field.Value.Texts ?? [])
                .Concat(field.Value.TextsR1 ?? [])
                .Concat(field.Value.TextsR2 ?? [])
                .Concat(field.Value.TextsR3 ?? [])
                .Distinct()
                .ToArray();

            if (allTexts.Length > 0)
            {
                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.Texts}",
                    Values = allTexts.OfType<object>().ToArray(),
                    FieldType = SearchFieldDataType.String,
                    IsCollection = true,
                    IsSortable = false,
                    IsSearchable = true,
                    IsFacetable = false,
                    SourceField = field
                };

                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.TextsR1}",
                    Values = (field.Value.TextsR1 ?? []).OfType<object>().ToArray(),
                    FieldType = SearchFieldDataType.String,
                    IsCollection = true,
                    IsSortable = false,
                    IsSearchable = true,
                    IsFacetable = false,
                    SourceField = field
                };

                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.TextsR2}",
                    Values = (field.Value.TextsR2 ?? []).OfType<object>().ToArray(),
                    FieldType = SearchFieldDataType.String,
                    IsCollection = true,
                    IsSortable = false,
                    IsSearchable = true,
                    IsFacetable = false,
                    SourceField = field
                };

                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.TextsR3}",
                    Values = (field.Value.TextsR3 ?? []).OfType<object>().ToArray(),
                    FieldType = SearchFieldDataType.String,
                    IsCollection = true,
                    IsSortable = false,
                    IsSearchable = true,
                    IsFacetable = false,
                    SourceField = field
                };

                // Sortable text field
                var sortableText = string.Join(" ", allTexts.Take(5)).ToLowerInvariant();
                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.Texts}{IndexConstants.FieldTypePostfix.Sortable}",
                    Values = [sortableText],
                    FieldType = SearchFieldDataType.String,
                    IsCollection = false,
                    IsSortable = true,
                    IsSearchable = false,
                    IsFacetable = false,
                    SourceField = field
                };
            }

            // Keywords
            if (field.Value.Keywords?.Any() is true)
            {
                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.Keywords}",
                    Values = field.Value.Keywords.OfType<object>().ToArray(),
                    FieldType = SearchFieldDataType.String,
                    IsCollection = true,
                    IsSortable = false,
                    IsSearchable = false,
                    IsFacetable = true,
                    SourceField = field
                };

                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.Keywords}{IndexConstants.FieldTypePostfix.Sortable}",
                    Values = [field.Value.Keywords.First()],
                    FieldType = SearchFieldDataType.String,
                    IsCollection = false,
                    IsSortable = true,
                    IsSearchable = false,
                    IsFacetable = false,
                    SourceField = field
                };
            }

            // Integers
            if (field.Value.Integers?.Any() is true)
            {
                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.Integers}",
                    Values = field.Value.Integers.OfType<object>().ToArray(),
                    FieldType = SearchFieldDataType.Int64,
                    IsCollection = true,
                    IsSortable = false,
                    IsSearchable = false,
                    IsFacetable = true,
                    SourceField = field
                };

                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.Integers}{IndexConstants.FieldTypePostfix.Sortable}",
                    Values = [field.Value.Integers.First()],
                    FieldType = SearchFieldDataType.Int64,
                    IsCollection = false,
                    IsSortable = true,
                    IsSearchable = false,
                    IsFacetable = false,
                    SourceField = field
                };
            }

            // Decimals
            if (field.Value.Decimals?.Any() is true)
            {
                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.Decimals}",
                    Values = field.Value.Decimals.OfType<object>().ToArray(),
                    FieldType = SearchFieldDataType.Double,
                    IsCollection = true,
                    IsSortable = false,
                    IsSearchable = false,
                    IsFacetable = true,
                    SourceField = field
                };

                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.Decimals}{IndexConstants.FieldTypePostfix.Sortable}",
                    Values = [field.Value.Decimals.First()],
                    FieldType = SearchFieldDataType.Double,
                    IsCollection = false,
                    IsSortable = true,
                    IsSearchable = false,
                    IsFacetable = false,
                    SourceField = field
                };
            }

            // DateTimeOffsets
            if (field.Value.DateTimeOffsets?.Any() is true)
            {
                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.DateTimeOffsets}",
                    Values = field.Value.DateTimeOffsets.OfType<object>().ToArray(),
                    FieldType = SearchFieldDataType.DateTimeOffset,
                    IsCollection = true,
                    IsSortable = false,
                    IsSearchable = false,
                    IsFacetable = true,
                    SourceField = field
                };

                yield return new IndexFieldMapping
                {
                    FieldName = $"{field.FieldName}{IndexConstants.FieldTypePostfix.DateTimeOffsets}{IndexConstants.FieldTypePostfix.Sortable}",
                    Values = [field.Value.DateTimeOffsets.First()],
                    FieldType = SearchFieldDataType.DateTimeOffset,
                    IsCollection = false,
                    IsSortable = true,
                    IsSearchable = false,
                    IsFacetable = false,
                    SourceField = field
                };
            }
        }
    }

    private string BuildUrl(string? route)
    {
        if (_baseUrl is null)
            return route ?? string.Empty;

        if (string.IsNullOrWhiteSpace(route))
            return _baseUrl;

        var path = route.StartsWith('/') ? route : $"/{route}";
        return $"{_baseUrl}{path}";
    }

    private Dictionary<string, string> ResolveContentUrls(Guid contentKey)
    {
        var urls = new Dictionary<string, string>();

        try
        {
            using var ctx = _umbracoContextFactory.EnsureUmbracoContext();
            var content = ctx.UmbracoContext.Content?.GetById(contentKey);
            if (content is null) return urls;

            foreach (var culture in content.Cultures.Keys)
            {
                var url = _publishedUrlProvider.GetUrl(content, UrlMode.Absolute, culture);
                if (!string.IsNullOrWhiteSpace(url) && url != "#")
                {
                    urls[culture] = url;
                }
            }

            var invariantUrl = _publishedUrlProvider.GetUrl(content, UrlMode.Absolute);
            if (!string.IsNullOrWhiteSpace(invariantUrl) && invariantUrl != "#")
            {
                urls[string.Empty] = invariantUrl;
            }
        }
        catch
        {
            // Fallback to BuildUrl if Umbraco context is unavailable (e.g. during rebuild)
        }

        return urls;
    }

    private static IndexField[] GetVariationFields(
        List<IGrouping<string, IndexField>> fieldsByFieldName,
        Variation variation)
    {
        return fieldsByFieldName.Select(g =>
        {
            var applicableFields = g.Where(f =>
                (variation.Culture is not null && variation.Segment is not null
                    && f.Culture == variation.Culture && f.Segment == variation.Segment)
                || (variation.Culture is not null && f.Culture == variation.Culture && f.Segment is null)
                || (variation.Segment is not null && f.Culture is null && f.Segment == variation.Segment)
                || (f.Culture is null && f.Segment is null)
            ).ToArray();

            if (applicableFields.Length == 0) return null;

            return new IndexField(
                g.Key,
                new IndexValue
                {
                    Texts = applicableFields.SelectMany(f => f.Value.Texts ?? []).ToArray().NullIfEmpty(),
                    TextsR1 = applicableFields.SelectMany(f => f.Value.TextsR1 ?? []).ToArray().NullIfEmpty(),
                    TextsR2 = applicableFields.SelectMany(f => f.Value.TextsR2 ?? []).ToArray().NullIfEmpty(),
                    TextsR3 = applicableFields.SelectMany(f => f.Value.TextsR3 ?? []).ToArray().NullIfEmpty(),
                    Keywords = applicableFields.SelectMany(f => f.Value.Keywords ?? []).ToArray().NullIfEmpty(),
                    Integers = applicableFields.SelectMany(f => f.Value.Integers ?? []).ToArray().NullIfEmpty(),
                    Decimals = applicableFields.SelectMany(f => f.Value.Decimals ?? []).ToArray().NullIfEmpty(),
                    DateTimeOffsets = applicableFields.SelectMany(f => f.Value.DateTimeOffsets ?? []).ToArray().NullIfEmpty(),
                },
                variation.Culture,
                variation.Segment
            );
        }).Where(f => f is not null).ToArray()!;
    }

    private static string[]? StripHtml(IEnumerable<string>? values)
        => values?
            .Select(v => v.StripHtml())
            .Where(v => v.Length > 0)
            .ToArray()
            .NullIfEmpty();

    private static bool IsSystemKeywordField(string fieldName)
        => IndexConstants.SystemKeywordFields.Contains(fieldName)
           || IndexConstants.SystemKeywordFieldPrefixes.Any(p => fieldName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsStructuralValue(string value)
        => Guid.TryParse(value, out _);
}

internal static class EnumerableNullExtensions
{
    public static T[]? NullIfEmpty<T>(this T[] array)
        => array.Length == 0 ? null : array;
}
