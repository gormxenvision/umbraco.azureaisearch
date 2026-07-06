using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Umbraco.AzureAISearch.Constants;
using Umbraco.AzureAISearch.Services.Factory;
using Umbraco.AzureAISearch.Services.IndexAliasResolver;
using Umbraco.Cms.Core.Sync;

namespace Umbraco.AzureAISearch.Services.IndexManager;

public interface IAzureAISearchIndexManager
{
    Task EnsureIndexAsync(string indexAlias);
    Task DeleteIndexAsync(string indexAlias);
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

    private static SearchIndex BuildIndex(string indexName)
    {
        var index = new SearchIndex(indexName)
        {
            Fields =
            [
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
