using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Sync;

namespace Umbraco.AzureAISearch.Services;

internal abstract class UmbracoAzureServiceBase(IServerRoleAccessor serverRoleAccessor)
{
    protected bool ShouldNotManipulateIndexes()
        => serverRoleAccessor.CurrentServerRole is ServerRole.Subscriber;
}
