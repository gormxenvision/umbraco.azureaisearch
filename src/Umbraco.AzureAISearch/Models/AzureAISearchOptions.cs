namespace Umbraco.AzureAISearch.Models;

public class AzureAISearchOptions
{
    public const string SectionName = "AzureAISearch";

    /// <summary>
    /// The Azure AI Search service endpoint URL (e.g., "https://your-service.search.windows.net").
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// The Azure AI Search admin API key.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The base URL for building absolute content URLs (e.g., "https://www.example.com").
    /// </summary>
    public required string BaseUrl { get; set; }

    /// <summary>
    /// Content type aliases to exclude from indexing.
    /// </summary>
    public string[] ExcludedContentTypes { get; set; } = [];

    /// <summary>
    /// Optional environment prefix for index names (e.g., "dev", "staging").
    /// </summary>
    public string? Environment { get; set; }
}
