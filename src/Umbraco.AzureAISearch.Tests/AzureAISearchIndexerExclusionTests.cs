using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.AzureAISearch.Models;
using Umbraco.AzureAISearch.Services.Factory;
using Umbraco.AzureAISearch.Services.IndexAliasResolver;
using Umbraco.AzureAISearch.Services.Indexer;
using Umbraco.AzureAISearch.Services.IndexManager;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Xunit;

namespace Umbraco.AzureAISearch.Tests;

public class AzureAISearchIndexerExclusionTests
{
    private readonly Mock<IAzureSearchClientFactory> _clientFactory = new();
    private readonly Mock<IAzureAISearchIndexManager> _indexManager = new();
    private readonly Mock<IIndexAliasResolver> _aliasResolver = new();
    private readonly DocumentMapper _documentMapper;
    private readonly Mock<IServerRoleAccessor> _serverRoleAccessor = new();
    private readonly Mock<IUmbracoContextFactory> _umbracoContextFactory = new();
    private readonly ILogger<AzureAISearchIndexer> _logger = NullLogger<AzureAISearchIndexer>.Instance;

    public AzureAISearchIndexerExclusionTests()
    {
        var options = Options.Create(new AzureAISearchOptions
        {
            Endpoint = "https://test.search.windows.net",
            Key = "key",
        });
        _documentMapper = new DocumentMapper(
            options,
            _umbracoContextFactory.Object,
            Mock.Of<Umbraco.Cms.Core.Routing.IPublishedUrlProvider>());

        _serverRoleAccessor.Setup(x => x.CurrentServerRole).Returns(ServerRole.Single);
        _aliasResolver.Setup(x => x.Resolve(It.IsAny<string>())).Returns("test-index");
    }

    private AzureAISearchIndexer CreateIndexer(string[] excludedContentTypes)
    {
        var options = Options.Create(new AzureAISearchOptions
        {
            Endpoint = "https://test.search.windows.net",
            Key = "test-key",
            ExcludedContentTypes = excludedContentTypes,
        });

        return new AzureAISearchIndexer(
            _clientFactory.Object,
            _indexManager.Object,
            _aliasResolver.Object,
            _documentMapper,
            options,
            _serverRoleAccessor.Object,
            _umbracoContextFactory.Object,
            _logger);
    }

    [Fact]
    public async Task AddOrUpdate_ExcludesDirectContentType()
    {
        var indexer = CreateIndexer(["settings"]);

        var fields = new List<IndexField>
        {
            new("contentTypeAlias", new IndexValue { Keywords = ["settings"] }, null, null),
        };

        // Should not throw or call search client
        await indexer.AddOrUpdateAsync(
            "test-alias", Guid.NewGuid(), UmbracoObjectTypes.Document,
            [new Variation(null, null)], fields, null);

        _clientFactory.Verify(x => x.GetSearchClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddOrUpdate_ExcludesChildOfExcludedContentType()
    {
        var settingsKey = Guid.NewGuid();
        var childKey = Guid.NewGuid();

        // Set up the Umbraco context to return ancestor content type
        var mockContentType = new Mock<IPublishedContentType>();
        mockContentType.Setup(x => x.Alias).Returns("settings");

        var mockAncestor = new Mock<IPublishedContent>();
        mockAncestor.Setup(x => x.ContentType).Returns(mockContentType.Object);

        var mockContentCache = new Mock<IPublishedContentCache>();
        mockContentCache.Setup(x => x.GetById(settingsKey)).Returns(mockAncestor.Object);

        var mockUmbracoContext = new Mock<IUmbracoContext>();
        mockUmbracoContext.Setup(x => x.Content).Returns(mockContentCache.Object);

        var ctxRef = new UmbracoContextReference(mockUmbracoContext.Object, false, Mock.Of<IUmbracoContextAccessor>());
        _umbracoContextFactory.Setup(x => x.EnsureUmbracoContext()).Returns(ctxRef);

        var indexer = CreateIndexer(["settings"]);

        // Child has a different content type, but its path includes the settings node
        var fields = new List<IndexField>
        {
            new("contentTypeAlias", new IndexValue { Keywords = ["blogPost"] }, null, null),
            new("pathIds", new IndexValue { Keywords = [settingsKey.ToString("D"), childKey.ToString("D")] }, null, null),
        };

        await indexer.AddOrUpdateAsync(
            "test-alias", childKey, UmbracoObjectTypes.Document,
            [new Variation(null, null)], fields, null);

        // Should be excluded — search client never called
        _clientFactory.Verify(x => x.GetSearchClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddOrUpdate_DoesNotExcludeWhenNoAncestorIsExcluded()
    {
        var parentKey = Guid.NewGuid();
        var childKey = Guid.NewGuid();

        var mockContentType = new Mock<IPublishedContentType>();
        mockContentType.Setup(x => x.Alias).Returns("homepage");

        var mockAncestor = new Mock<IPublishedContent>();
        mockAncestor.Setup(x => x.ContentType).Returns(mockContentType.Object);

        var mockContentCache = new Mock<IPublishedContentCache>();
        mockContentCache.Setup(x => x.GetById(parentKey)).Returns(mockAncestor.Object);

        var mockUmbracoContext = new Mock<IUmbracoContext>();
        mockUmbracoContext.Setup(x => x.Content).Returns(mockContentCache.Object);

        var ctxRef = new UmbracoContextReference(mockUmbracoContext.Object, false, Mock.Of<IUmbracoContextAccessor>());
        _umbracoContextFactory.Setup(x => x.EnsureUmbracoContext()).Returns(ctxRef);

        // Mock the search client — IndexDocumentsAsync will throw but that's fine,
        // we only need to verify the exclusion check was passed
        _clientFactory.Setup(x => x.GetSearchClient(It.IsAny<string>())).Returns(Mock.Of<SearchClient>());

        var indexer = CreateIndexer(["settings"]);

        var fields = new List<IndexField>
        {
            new("contentTypeAlias", new IndexValue { Keywords = ["blogPost"] }, null, null),
            new("pathIds", new IndexValue { Keywords = [parentKey.ToString("D"), childKey.ToString("D")] }, null, null),
            new("body", new IndexValue { Texts = ["Hello world"] }, null, null),
        };

        // The call may throw after passing exclusion (due to incomplete mocks for indexing),
        // but if it gets past IsExcluded, GetSearchClient will be invoked
        try
        {
            await indexer.AddOrUpdateAsync(
                "test-alias", childKey, UmbracoObjectTypes.Document,
                [new Variation(null, null)], fields, null);
        }
        catch (NullReferenceException)
        {
            // Expected — downstream indexing mocks are incomplete
        }

        // Should NOT be excluded — search client should be called
        _clientFactory.Verify(x => x.GetSearchClient(It.IsAny<string>()), Times.Once);
    }
}
