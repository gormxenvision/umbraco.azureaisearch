using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.AzureAISearch.Models;
using Umbraco.AzureAISearch.NotificationHandlers;
using Umbraco.AzureAISearch.Services.Factory;
using Umbraco.AzureAISearch.Services.IndexAliasResolver;
using Umbraco.AzureAISearch.Services.Indexer;
using Umbraco.AzureAISearch.Services.IndexManager;
using Umbraco.AzureAISearch.Services.Searcher;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Search.Core.Configuration;
using Umbraco.Cms.Search.Core.Notifications;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;

namespace Umbraco.AzureAISearch.Extensions;

public static class ServiceCollectionExtensions
{
    public static IUmbracoBuilder AddUmbracoAzureAISearch(
        this IUmbracoBuilder builder, IConfiguration configuration)
    {
        builder.Services.Configure<AzureAISearchOptions>(
            configuration.GetSection(AzureAISearchOptions.SectionName));

        builder.Services.AddSingleton<IAzureSearchClientFactory, AzureSearchClientFactory>();
        builder.Services.AddSingleton<IIndexAliasResolver, IndexAliasResolver>();
        builder.Services.AddSingleton<IAzureAISearchIndexManager, AzureAISearchIndexManager>();
        builder.Services.AddSingleton<DocumentMapper>();

        // Register concrete types so the IndexerResolver can resolve them
        builder.Services.AddSingleton<AzureAISearchIndexer>();
        builder.Services.AddSingleton<IAzureAISearchIndexer>(sp => sp.GetRequiredService<AzureAISearchIndexer>());
        builder.Services.AddTransient<AzureAISearchSearcher>();
        builder.Services.AddTransient<IAzureAISearchSearcher>(sp => sp.GetRequiredService<AzureAISearchSearcher>());

        // Register with IndexOptions so the IIndexerResolver/ISearcherResolver can find our implementations
        builder.Services.Configure<IndexOptions>(options =>
        {
            options.RegisterContentIndex<AzureAISearchIndexer, AzureAISearchSearcher, IPublishedContentChangeStrategy>(
                Umbraco.Cms.Search.Core.Constants.IndexAliases.PublishedContent,
                UmbracoObjectTypes.Document);
        });

        builder.AddNotificationHandler<UmbracoApplicationStartingNotification, EnsureIndexNotificationHandler>();
        builder.AddNotificationAsyncHandler<IndexRebuildStartingNotification, RebuildIndexNotificationHandler>();

        return builder;
    }

    /// <summary>
    /// Triggers a full index rebuild on application startup.
    /// Call this after <see cref="AddUmbracoAzureAISearch"/> to populate the index on first run.
    /// </summary>
    public static IUmbracoBuilder RebuildAzureAISearchOnStartup(this IUmbracoBuilder builder)
    {
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, RebuildIndicesOnStartupNotificationHandler>();
        return builder;
    }
}
