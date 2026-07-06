using Umbraco.AzureAISearch.Services.IndexManager;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.AzureAISearch.NotificationHandlers;

internal sealed class EnsureIndexNotificationHandler(
    IAzureAISearchIndexManager indexManager)
    : INotificationHandler<UmbracoApplicationStartingNotification>
{
    public void Handle(UmbracoApplicationStartingNotification notification)
    {
        indexManager.EnsureIndexAsync(Umbraco.Cms.Search.Core.Constants.IndexAliases.PublishedContent);
    }
}
