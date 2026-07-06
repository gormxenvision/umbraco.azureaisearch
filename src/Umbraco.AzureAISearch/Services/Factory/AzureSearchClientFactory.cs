using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Options;
using Umbraco.AzureAISearch.Models;

namespace Umbraco.AzureAISearch.Services.Factory;

public interface IAzureSearchClientFactory
{
    SearchIndexClient GetSearchIndexClient();
    SearchClient GetSearchClient(string indexName);
}

internal sealed class AzureSearchClientFactory : IAzureSearchClientFactory
{
    private readonly SearchIndexClient _indexClient;

    public AzureSearchClientFactory(IOptions<AzureAISearchOptions> options)
    {
        var opts = options.Value;
        var endpoint = new Uri(opts.Endpoint);
        var credential = new AzureKeyCredential(opts.Key);
        _indexClient = new SearchIndexClient(endpoint, credential);
    }

    public SearchIndexClient GetSearchIndexClient() => _indexClient;

    public SearchClient GetSearchClient(string indexName)
        => _indexClient.GetSearchClient(indexName);
}
