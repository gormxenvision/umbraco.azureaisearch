using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.AzureAISearch.Constants;
using Umbraco.AzureAISearch.Models;
using Umbraco.AzureAISearch.Services.Factory;
using Umbraco.AzureAISearch.Services.IndexAliasResolver;
using Umbraco.AzureAISearch.Services.IndexManager;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.Services;

namespace Umbraco.AzureAISearch.Services.Indexer;

internal sealed class AzureAISearchIndexer(
    IAzureSearchClientFactory clientFactory,
    IAzureAISearchIndexManager indexManager,
    IIndexAliasResolver aliasResolver,
    DocumentMapper documentMapper,
    IOptions<AzureAISearchOptions> options,
    IServerRoleAccessor serverRoleAccessor,
    IUmbracoContextFactory umbracoContextFactory,
    ILogger<AzureAISearchIndexer> logger)
    : UmbracoAzureServiceBase(serverRoleAccessor), IAzureAISearchIndexer
{
    private readonly string[] _excludedContentTypes = options.Value.ExcludedContentTypes;

    public async Task AddOrUpdateAsync(
        string indexAlias,
        Guid id,
        UmbracoObjectTypes objectType,
        IEnumerable<Variation> variations,
        IEnumerable<IndexField> fields,
        ContentProtection? protection)
    {
        if (ShouldNotManipulateIndexes()) return;

        var fieldsList = fields.ToList();

        if (IsExcluded(id, fieldsList)) return;

        var indexName = aliasResolver.Resolve(indexAlias);
        var searchClient = clientFactory.GetSearchClient(indexName);

        var mappingResult = documentMapper.MapToDocuments(id, objectType, variations, fieldsList, protection);

        if (mappingResult.Documents.Count == 0) return;

        await indexManager.EnsureFieldsExistAsync(indexAlias, mappingResult.FieldMappings);

        try
        {
            var batch = IndexDocumentsBatch.MergeOrUpload(mappingResult.Documents);
            var result = await searchClient.IndexDocumentsAsync(batch,
                new IndexDocumentsOptions { ThrowOnAnyError = false });

            var failed = result.Value.Results.Where(r => !r.Succeeded).ToList();
            if (failed.Count > 0)
            {
                foreach (var failure in failed)
                {
                    logger.LogError(
                        "Failed to index document {DocumentKey} for content {Id} into {IndexAlias}: [{StatusCode}] {ErrorMessage}",
                        failure.Key, id, indexAlias, failure.Status, failure.ErrorMessage);
                }
            }
            else
            {
                logger.LogDebug("Indexed {Count} documents for {Id} in {IndexAlias}",
                    mappingResult.Documents.Count, id, indexAlias);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to index content {Id} in {IndexAlias}", id, indexAlias);
            throw;
        }
    }

    public async Task DeleteAsync(string indexAlias, IEnumerable<Guid> ids)
    {
        if (ShouldNotManipulateIndexes()) return;

        var indexName = aliasResolver.Resolve(indexAlias);
        var searchClient = clientFactory.GetSearchClient(indexName);

        var idList = ids.ToList();
        var pathIdField = $"{IndexConstants.FieldNames.PathIds}{IndexConstants.FieldTypePostfix.Keywords}";

        var pathIdFilters = idList.Select(id => $"{pathIdField}/any(p: p eq '{id:D}')");
        var filter = string.Join(" or ", pathIdFilters);

        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = 10000,
            Select = { IndexConstants.FieldNames.Id }
        };

        var searchResult = await searchClient.SearchAsync<SearchDocument>("*", searchOptions);
        var documentIds = new List<string>();

        await foreach (var result in searchResult.Value.GetResultsAsync())
        {
            if (result.Document.TryGetValue(IndexConstants.FieldNames.Id, out var idValue) && idValue != null)
            {
                documentIds.Add(idValue.ToString()!);
            }
        }

        if (documentIds.Count > 0)
        {
            logger.LogDebug("Deleting {Count} documents from {IndexAlias} for ids {Ids}",
                documentIds.Count, indexAlias, idList);
            await searchClient.DeleteDocumentsAsync(IndexConstants.FieldNames.Id, documentIds,
                new IndexDocumentsOptions { ThrowOnAnyError = false });
        }
    }

    public async Task ResetAsync(string indexAlias)
    {
        await indexManager.DeleteIndexAsync(indexAlias);
        await indexManager.EnsureIndexAsync(indexAlias);
    }

    public async Task<IndexMetadata> GetMetadataAsync(string indexAlias)
    {
        var resolvedName = aliasResolver.Resolve(indexAlias);
        var indexClient = clientFactory.GetSearchIndexClient();

        try
        {
            var response = await indexClient.GetIndexStatisticsAsync(resolvedName);
            var documentCount = response.Value.DocumentCount;
            var healthStatus = documentCount > 0 ? HealthStatus.Healthy : HealthStatus.Empty;
            return new IndexMetadata(documentCount, healthStatus, IndexConstants.ProviderIdentifier);
        }
        catch
        {
            return new IndexMetadata(0, HealthStatus.Unknown, IndexConstants.ProviderIdentifier);
        }
    }

    private bool IsExcluded(Guid contentKey, List<IndexField> fields)
    {
        if (_excludedContentTypes.Length == 0) return false;

        // First try field-based check (fast path)
        var contentTypeField = fields.FirstOrDefault(f =>
            string.Equals(f.FieldName, "contentTypeAlias", StringComparison.OrdinalIgnoreCase));

        if (contentTypeField?.Value.Keywords?.FirstOrDefault() is { } alias)
        {
            if (_excludedContentTypes.Contains(alias, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        // Look up the content directly to check its type and ancestors
        using var ctx = umbracoContextFactory.EnsureUmbracoContext();
        var contentCache = ctx.UmbracoContext.Content;

        if (contentCache is null) return false;

        var content = contentCache.GetById(contentKey);
        if (content is null) return false;

        // Check the content's own type (in case field-based check missed it)
        if (_excludedContentTypes.Contains(content.ContentType.Alias, StringComparer.OrdinalIgnoreCase))
            return true;

        // Walk up the tree to check ancestors
        var ancestor = content.Parent;
        while (ancestor is not null)
        {
            if (_excludedContentTypes.Contains(ancestor.ContentType.Alias, StringComparer.OrdinalIgnoreCase))
                return true;

            ancestor = ancestor.Parent;
        }

        return false;
    }
}
