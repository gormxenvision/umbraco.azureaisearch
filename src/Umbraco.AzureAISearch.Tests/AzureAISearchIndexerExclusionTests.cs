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
        var contentKey = Guid.NewGuid();

        // Mock content with "settings" content type
        var mockContentType = new Mock<IPublishedContentType>();
        mockContentType.Setup(x => x.Alias).Returns("settings");

        var mockContent = new Mock<IPublishedContent>();
        mockContent.Setup(x => x.ContentType).Returns(mockContentType.Object);
        mockContent.Setup(x => x.Parent).Returns((IPublishedContent?)null);

        var mockContentCache = new Mock<IPublishedContentCache>();
        mockContentCache.Setup(x => x.GetById(contentKey)).Returns(mockContent.Object);

        var mockUmbracoContext = new Mock<IUmbracoContext>();
        mockUmbracoContext.Setup(x => x.Content).Returns(mockContentCache.Object);

        var ctxRef = new UmbracoContextReference(mockUmbracoContext.Object, false, Mock.Of<IUmbracoContextAccessor>());
        _umbracoContextFactory.Setup(x => x.EnsureUmbracoContext()).Returns(ctxRef);

        var indexer = CreateIndexer(["settings"]);

        var fields = new List<IndexField>
        {
            new("body", new IndexValue { Texts = ["Some content"] }, null, null),
        };

        await indexer.AddOrUpdateAsync(
            "test-alias", contentKey, UmbracoObjectTypes.Document,
            [new Variation(null, null)], fields, null);

        _clientFactory.Verify(x => x.GetSearchClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddOrUpdate_ExcludesChildOfExcludedContentType()
    {
        var childKey = Guid.NewGuid();

        // Mock parent with "settings" content type
        var settingsContentType = new Mock<IPublishedContentType>();
        settingsContentType.Setup(x => x.Alias).Returns("settings");

        var mockParent = new Mock<IPublishedContent>();
        mockParent.Setup(x => x.ContentType).Returns(settingsContentType.Object);
        mockParent.Setup(x => x.Parent).Returns((IPublishedContent?)null);

        // Mock child with "blogPost" content type, parented under settings
        var childContentType = new Mock<IPublishedContentType>();
        childContentType.Setup(x => x.Alias).Returns("blogPost");

        var mockChild = new Mock<IPublishedContent>();
        mockChild.Setup(x => x.ContentType).Returns(childContentType.Object);
        mockChild.Setup(x => x.Parent).Returns(mockParent.Object);

        var mockContentCache = new Mock<IPublishedContentCache>();
        mockContentCache.Setup(x => x.GetById(childKey)).Returns(mockChild.Object);

        var mockUmbracoContext = new Mock<IUmbracoContext>();
        mockUmbracoContext.Setup(x => x.Content).Returns(mockContentCache.Object);

        var ctxRef = new UmbracoContextReference(mockUmbracoContext.Object, false, Mock.Of<IUmbracoContextAccessor>());
        _umbracoContextFactory.Setup(x => x.EnsureUmbracoContext()).Returns(ctxRef);

        var indexer = CreateIndexer(["settings"]);

        var fields = new List<IndexField>
        {
            new("body", new IndexValue { Texts = ["Child content"] }, null, null),
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
        var childKey = Guid.NewGuid();

        // Mock parent with "homepage" content type (not excluded)
        var homepageContentType = new Mock<IPublishedContentType>();
        homepageContentType.Setup(x => x.Alias).Returns("homepage");

        var mockParent = new Mock<IPublishedContent>();
        mockParent.Setup(x => x.ContentType).Returns(homepageContentType.Object);
        mockParent.Setup(x => x.Parent).Returns((IPublishedContent?)null);

        // Mock child with "blogPost" content type
        var childContentType = new Mock<IPublishedContentType>();
        childContentType.Setup(x => x.Alias).Returns("blogPost");

        var mockChild = new Mock<IPublishedContent>();
        mockChild.Setup(x => x.ContentType).Returns(childContentType.Object);
        mockChild.Setup(x => x.Parent).Returns(mockParent.Object);

        var mockContentCache = new Mock<IPublishedContentCache>();
        mockContentCache.Setup(x => x.GetById(childKey)).Returns(mockChild.Object);

        var mockUmbracoContext = new Mock<IUmbracoContext>();
        mockUmbracoContext.Setup(x => x.Content).Returns(mockContentCache.Object);

        var ctxRef = new UmbracoContextReference(mockUmbracoContext.Object, false, Mock.Of<IUmbracoContextAccessor>());
        _umbracoContextFactory.Setup(x => x.EnsureUmbracoContext()).Returns(ctxRef);

        _clientFactory.Setup(x => x.GetSearchClient(It.IsAny<string>())).Returns(Mock.Of<SearchClient>());

        var indexer = CreateIndexer(["settings"]);

        var fields = new List<IndexField>
        {
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
