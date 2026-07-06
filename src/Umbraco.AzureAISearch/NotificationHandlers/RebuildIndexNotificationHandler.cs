using Umbraco.AzureAISearch.Services.IndexManager;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Search.Core.Notifications;

namespace Umbraco.AzureAISearch.NotificationHandlers;

internal sealed class RebuildIndexNotificationHandler(
    IAzureAISearchIndexManager indexManager)
    : INotificationAsyncHandler<IndexRebuildStartingNotification>
{
    public async Task HandleAsync(IndexRebuildStartingNotification notification, CancellationToken cancellationToken)
    {
        await indexManager.DeleteIndexAsync(notification.IndexAlias);
        await indexManager.EnsureIndexAsync(notification.IndexAlias);
    }
}
