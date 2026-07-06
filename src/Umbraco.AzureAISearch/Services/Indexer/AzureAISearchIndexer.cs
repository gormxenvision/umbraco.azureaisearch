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

        if (IsExcluded(fieldsList)) return;

        var indexName = aliasResolver.Resolve(indexAlias);
        var searchClient = clientFactory.GetSearchClient(indexName);

        var documents = documentMapper.MapToDocuments(id, objectType, variations, fieldsList, protection);

        if (documents.Count == 0) return;

        try
        {
            var batch = IndexDocumentsBatch.MergeOrUpload(documents);
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
                    documents.Count, id, indexAlias);
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
        var keyFilters = idList.Select(id => $"{IndexConstants.FieldNames.Key} eq '{id:D}'");
        var filter = string.Join(" or ", keyFilters);

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

    private bool IsExcluded(List<IndexField> fields)
    {
        if (_excludedContentTypes.Length == 0) return false;

        var contentTypeField = fields.FirstOrDefault(f =>
            string.Equals(f.FieldName, "contentTypeAlias", StringComparison.OrdinalIgnoreCase));

        if (contentTypeField?.Value.Keywords?.FirstOrDefault() is { } alias)
        {
            return _excludedContentTypes.Contains(alias, StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }
}
