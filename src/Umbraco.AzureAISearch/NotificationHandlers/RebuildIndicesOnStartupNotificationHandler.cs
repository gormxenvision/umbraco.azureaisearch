using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;

namespace Umbraco.AzureAISearch.NotificationHandlers;

internal sealed class RebuildIndicesOnStartupNotificationHandler(
    IContentIndexingService contentIndexingService,
    IOriginProvider originProvider)
    : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        var origin = originProvider.GetCurrent();
        contentIndexingService.Rebuild(
            Umbraco.Cms.Search.Core.Constants.IndexAliases.PublishedContent, origin);
    }
}
