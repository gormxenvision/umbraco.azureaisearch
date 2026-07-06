using Microsoft.Extensions.Options;
using Umbraco.AzureAISearch.Models;
using Umbraco.AzureAISearch.Services.Indexer;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Xunit;

namespace Umbraco.AzureAISearch.Tests;

public class DocumentMapperTests
{
    private DocumentMapper CreateMapper(string baseUrl = "https://www.example.com")
    {
        var options = Options.Create(new AzureAISearchOptions
        {
            Endpoint = "https://test.search.windows.net",
            Key = "test-key",
            BaseUrl = baseUrl,
        });
        return new DocumentMapper(options);
    }

    [Fact]
    public void MapToDocuments_ProducesDocumentPerVariation()
    {
        var mapper = CreateMapper();
        var variations = new List<Variation>
        {
            new("en-US", null),
            new("da-DK", null),
        };
        var fields = new List<IndexField>
        {
            new("body", new IndexValue { TextsR1 = ["Hello"] }, "en-US", null),
            new("body", new IndexValue { TextsR1 = ["Hej"] }, "da-DK", null),
        };

        var docs = mapper.MapToDocuments(Guid.NewGuid(), UmbracoObjectTypes.Document, variations, fields, null);

        Assert.Equal(2, docs.Count);
    }

    [Fact]
    public void MapToDocuments_BuildsAbsoluteUrl_WithRoute()
    {
        var mapper = CreateMapper("https://www.example.com");
        var variations = new List<Variation> { new(null, null) };
        var fields = new List<IndexField>
        {
            new("url", new IndexValue { Keywords = ["/about-us"] }, null, null),
        };

        var docs = mapper.MapToDocuments(Guid.NewGuid(), UmbracoObjectTypes.Document, variations, fields, null);

        Assert.Equal("https://www.example.com/about-us", docs[0]["url"]?.ToString());
    }

    [Fact]
    public void MapToDocuments_StripsHtmlFromContent()
    {
        var mapper = CreateMapper();
        var variations = new List<Variation> { new(null, null) };
        var fields = new List<IndexField>
        {
            new("body", new IndexValue { Texts = ["<p>Hello <strong>World</strong></p>"] }, null, null),
        };

        var docs = mapper.MapToDocuments(Guid.NewGuid(), UmbracoObjectTypes.Document, variations, fields, null);

        var content = docs[0]["content"]?.ToString();
        Assert.NotNull(content);
        Assert.DoesNotContain("<", content);
        Assert.Contains("Hello World", content);
    }

    [Fact]
    public void MapToDocuments_CategorizesRelevanceTiers()
    {
        var mapper = CreateMapper();
        var variations = new List<Variation> { new(null, null) };
        var fields = new List<IndexField>
        {
            new("title", new IndexValue { TextsR1 = ["Title Text"] }, null, null),
            new("subtitle", new IndexValue { TextsR2 = ["Subtitle Text"] }, null, null),
            new("body", new IndexValue { TextsR3 = ["Body Text"] }, null, null),
        };

        var docs = mapper.MapToDocuments(Guid.NewGuid(), UmbracoObjectTypes.Document, variations, fields, null);

        var r1 = docs[0]["contentR1"] as string[];
        var r2 = docs[0]["contentR2"] as string[];
        var r3 = docs[0]["contentR3"] as string[];

        Assert.Contains("Title Text", r1!);
        Assert.Contains("Subtitle Text", r2!);
        Assert.Contains("Body Text", r3!);
    }

    [Fact]
    public void MapToDocuments_ContentFieldContainsAllText()
    {
        var mapper = CreateMapper();
        var variations = new List<Variation> { new(null, null) };
        var fields = new List<IndexField>
        {
            new("title", new IndexValue { TextsR1 = ["Title"] }, null, null),
            new("body", new IndexValue { TextsR3 = ["Body"] }, null, null),
        };

        var docs = mapper.MapToDocuments(Guid.NewGuid(), UmbracoObjectTypes.Document, variations, fields, null);

        var content = docs[0]["content"]?.ToString();
        Assert.Contains("Title", content);
        Assert.Contains("Body", content);
    }
}
