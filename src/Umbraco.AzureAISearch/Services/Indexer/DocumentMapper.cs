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

    public List<SearchDocument> MapToDocuments(
        Guid contentKey,
        UmbracoObjectTypes objectType,
        IEnumerable<Variation> variations,
        IEnumerable<IndexField> fields,
        ContentProtection? protection)
    {
        var fieldsByFieldName = fields.GroupBy(f => f.FieldName).ToList();
        var documents = new List<SearchDocument>();

        // Resolve the absolute URL from Umbraco's published content routing
        var resolvedUrls = ResolveContentUrls(contentKey);

        foreach (var variation in variations)
        {
            var culture = variation.Culture ?? IndexConstants.Variation.InvariantCulture;
            var segment = variation.Segment ?? IndexConstants.Variation.DefaultSegment;
            var documentId = $"{contentKey:D}_{culture}_{segment}";

            // Gather all text values per relevance tier for this variation
            var allTexts = new List<string>();
            var textsR1 = new List<string>();
            var textsR2 = new List<string>();
            var textsR3 = new List<string>();
            string? title = null;
            string? route = null;

            var variationFields = GetVariationFields(fieldsByFieldName, variation);

            foreach (var field in variationFields)
            {
                // Collect texts by relevance tier
                if (field.Value.TextsR1 is { } r1)
                {
                    var stripped = StripHtmlList(r1);
                    textsR1.AddRange(stripped);
                    allTexts.AddRange(stripped);
                }

                if (field.Value.TextsR2 is { } r2)
                {
                    var stripped = StripHtmlList(r2);
                    textsR2.AddRange(stripped);
                    allTexts.AddRange(stripped);
                }

                if (field.Value.TextsR3 is { } r3)
                {
                    var stripped = StripHtmlList(r3);
                    textsR3.AddRange(stripped);
                    allTexts.AddRange(stripped);
                }

                if (field.Value.Texts is { } texts)
                {
                    var stripped = StripHtmlList(texts);
                    allTexts.AddRange(stripped);
                }

                // Extract title from the first R1 text (highest relevance = typically the name)
                if (title is null && field.Value.TextsR1 is { } titleTexts && titleTexts.Any())
                {
                    title = titleTexts.First().StripHtml();
                }

                // Extract route/URL from keywords (nodeName field handling)
                if (field.FieldName == "url" && field.Value.Keywords is { } urlKeywords && urlKeywords.Any())
                {
                    route = urlKeywords.First();
                }
            }

            // If no title from R1, try the first text
            title ??= allTexts.FirstOrDefault() ?? string.Empty;

            // Resolve URL: prefer Umbraco routing, then field-extracted route, then fallback BaseUrl
            var url = (variation.Culture is not null && resolvedUrls.TryGetValue(variation.Culture, out var cultUrl) ? cultUrl : null)
                      ?? (resolvedUrls.TryGetValue(string.Empty, out var invUrl) ? invUrl : null)
                      ?? BuildUrl(route);
            var content = string.Join(" ", allTexts);

            // Access keys
            var accessKeys = protection?.AccessIds?.Any() is true
                ? protection.AccessIds.Select(g => g.ToString("D")).ToArray()
                : new[] { Guid.Empty.ToString("D") };

            var doc = new SearchDocument
            {
                [IndexConstants.FieldNames.Id] = documentId,
                [IndexConstants.FieldNames.Key] = contentKey.ToString("D"),
                [IndexConstants.FieldNames.ObjectType] = objectType.ToString(),
                [IndexConstants.FieldNames.Culture] = culture,
                [IndexConstants.FieldNames.Segment] = segment,
                [IndexConstants.FieldNames.Title] = title,
                [IndexConstants.FieldNames.Content] = content,
                [IndexConstants.FieldNames.ContentR1] = textsR1.ToArray(),
                [IndexConstants.FieldNames.ContentR2] = textsR2.ToArray(),
                [IndexConstants.FieldNames.ContentR3] = textsR3.ToArray(),
                [IndexConstants.FieldNames.Url] = url,
                [IndexConstants.FieldNames.AccessKeys] = accessKeys,
            };

            documents.Add(doc);
        }

        return documents;
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

            // Get URL for each culture the content is published in
            foreach (var culture in content.Cultures.Keys)
            {
                var url = _publishedUrlProvider.GetUrl(content, UrlMode.Absolute, culture);
                if (!string.IsNullOrWhiteSpace(url) && url != "#")
                {
                    urls[culture] = url;
                }
            }

            // Also try invariant (stored with empty string key)
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
                (variation.Culture is not null && f.Culture == variation.Culture && (f.Segment is null || f.Segment == variation.Segment))
                || (f.Culture is null && (f.Segment is null || f.Segment == variation.Segment))
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

    private static List<string> StripHtmlList(IEnumerable<string> values)
        => values
            .Select(v => v.StripHtml())
            .Where(v => v.Length > 0)
            .ToList();
}

internal static class EnumerableNullExtensions
{
    public static T[]? NullIfEmpty<T>(this T[] array)
        => array.Length == 0 ? null : array;
}
