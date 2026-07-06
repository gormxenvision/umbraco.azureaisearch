using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Umbraco.AzureAISearch.Constants;
using Umbraco.AzureAISearch.Models;
using Umbraco.AzureAISearch.Services.Factory;
using Umbraco.AzureAISearch.Services.IndexAliasResolver;
using Umbraco.Cms.Core.Sync;

namespace Umbraco.AzureAISearch.Services.IndexManager;

public interface IAzureAISearchIndexManager
{
    Task EnsureIndexAsync(string indexAlias);
    Task DeleteIndexAsync(string indexAlias);
    Task EnsureFieldsExistAsync(string indexAlias, List<IndexFieldMapping> fieldMappings);
}

internal sealed class AzureAISearchIndexManager(
    IAzureSearchClientFactory clientFactory,
    IIndexAliasResolver aliasResolver,
    IServerRoleAccessor serverRoleAccessor,
    ILogger<AzureAISearchIndexManager> logger)
    : UmbracoAzureServiceBase(serverRoleAccessor), IAzureAISearchIndexManager
{
    public async Task EnsureIndexAsync(string indexAlias)
    {
        if (ShouldNotManipulateIndexes()) return;

        var indexName = aliasResolver.Resolve(indexAlias);
        var indexClient = clientFactory.GetSearchIndexClient();

        try
        {
            await indexClient.GetIndexAsync(indexName);
            logger.LogDebug("Index {IndexName} already exists", indexName);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            logger.LogInformation("Creating index {IndexName}", indexName);
            var index = BuildIndex(indexName);
            await indexClient.CreateIndexAsync(index);
        }
    }

    public async Task DeleteIndexAsync(string indexAlias)
    {
        if (ShouldNotManipulateIndexes()) return;

        var indexName = aliasResolver.Resolve(indexAlias);
        var indexClient = clientFactory.GetSearchIndexClient();

        try
        {
            await indexClient.DeleteIndexAsync(indexName);
            logger.LogInformation("Deleted index {IndexName}", indexName);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            logger.LogDebug("Index {IndexName} does not exist, nothing to delete", indexName);
        }
    }

    public async Task EnsureFieldsExistAsync(string indexAlias, List<IndexFieldMapping> fieldMappings)
    {
        if (ShouldNotManipulateIndexes() || fieldMappings.Count == 0) return;

        var indexName = aliasResolver.Resolve(indexAlias);
        var indexClient = clientFactory.GetSearchIndexClient();

        SearchIndex index;
        try
        {
            var response = await indexClient.GetIndexAsync(indexName);
            index = response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            logger.LogWarning("Index {IndexName} not found when ensuring fields exist", indexName);
            return;
        }

        var currentFieldNames = index.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingMappings = fieldMappings
            .Where(m => !currentFieldNames.Contains(m.FieldName))
            .GroupBy(m => m.FieldName)
            .Select(g => g.First())
            .ToList();

        if (missingMappings.Count == 0) return;

        foreach (var mapping in missingMappings)
        {
            var fieldType = mapping.IsCollection
                ? SearchFieldDataType.Collection(mapping.FieldType)
                : mapping.FieldType;

            var field = new SearchField(mapping.FieldName, fieldType)
            {
                IsFilterable = !mapping.IsSortable,
                IsSortable = mapping.IsSortable,
                IsFacetable = mapping.IsFacetable,
                IsSearchable = mapping.IsSearchable,
            };

            index.Fields.Add(field);
        }

        logger.LogInformation("Adding {Count} new fields to index {IndexName}", missingMappings.Count, indexName);
        await indexClient.CreateOrUpdateIndexAsync(index);
    }

    private static SearchIndex BuildIndex(string indexName)
    {
        var index = new SearchIndex(indexName)
        {
            Fields =
            [
                // Core identity fields
                new SearchField(IndexConstants.FieldNames.Id, SearchFieldDataType.String)
                    { IsKey = true, IsFilterable = true },
                new SearchField(IndexConstants.FieldNames.Key, SearchFieldDataType.String)
                    { IsFilterable = true, IsSortable = true },
                new SearchField(IndexConstants.FieldNames.ObjectType, SearchFieldDataType.String)
                    { IsFilterable = true },
                new SearchField(IndexConstants.FieldNames.Culture, SearchFieldDataType.String)
                    { IsFilterable = true },
                new SearchField(IndexConstants.FieldNames.Segment, SearchFieldDataType.String)
                    { IsFilterable = true },
                new SearchField(IndexConstants.FieldNames.AccessKeys, SearchFieldDataType.Collection(SearchFieldDataType.String))
                    { IsFilterable = true },

                // Foundry-compatible fields
                new SearchField(IndexConstants.FieldNames.Title, SearchFieldDataType.String)
                    { IsSearchable = true, IsFilterable = false, IsFacetable = false },
                new SearchField(IndexConstants.FieldNames.Content, SearchFieldDataType.String)
                    { IsSearchable = true, IsFilterable = false, IsFacetable = false },
                new SearchField(IndexConstants.FieldNames.ContentR1, SearchFieldDataType.Collection(SearchFieldDataType.String))
                    { IsSearchable = true, IsFilterable = false, IsFacetable = false },
                new SearchField(IndexConstants.FieldNames.ContentR2, SearchFieldDataType.Collection(SearchFieldDataType.String))
                    { IsSearchable = true, IsFilterable = false, IsFacetable = false },
                new SearchField(IndexConstants.FieldNames.ContentR3, SearchFieldDataType.Collection(SearchFieldDataType.String))
                    { IsSearchable = true, IsFilterable = false, IsFacetable = false },
                new SearchField(IndexConstants.FieldNames.Url, SearchFieldDataType.String)
                    { IsSearchable = false, IsFilterable = false, IsFacetable = false },

                // PathIds for cascade delete
                new SearchField($"{IndexConstants.FieldNames.PathIds}{IndexConstants.FieldTypePostfix.Keywords}", SearchFieldDataType.Collection(SearchFieldDataType.String))
                    { IsFilterable = true, IsFacetable = false, IsSearchable = false },
            ]
        };

        var scoringProfile = new ScoringProfile(IndexConstants.ScoringProfiles.RelevanceBoost)
        {
            TextWeights = new TextWeights(new Dictionary<string, double>
            {
                { IndexConstants.FieldNames.ContentR1, 4.0 },
                { IndexConstants.FieldNames.ContentR2, 3.0 },
                { IndexConstants.FieldNames.ContentR3, 2.0 },
                { IndexConstants.FieldNames.Content, 1.0 },
            })
        };

        index.ScoringProfiles.Add(scoringProfile);
        index.DefaultScoringProfile = IndexConstants.ScoringProfiles.RelevanceBoost;

        return index;
    }
}
