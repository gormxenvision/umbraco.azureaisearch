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
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Search.Core.Notifications;
using Umbraco.Cms.Search.Core.Services;

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
        builder.Services.AddSingleton<IAzureAISearchIndexer, AzureAISearchIndexer>();
        builder.Services.AddSingleton<IIndexer, AzureAISearchIndexer>();
        builder.Services.AddTransient<IAzureAISearchSearcher, AzureAISearchSearcher>();
        builder.Services.AddTransient<ISearcher, AzureAISearchSearcher>();

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
