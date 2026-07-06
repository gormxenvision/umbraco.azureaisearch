# Umbraco.AzureAISearch

Azure AI Search provider for [Umbraco Search](https://github.com/umbraco/Umbraco.Cms.Search) — designed to work seamlessly with **Microsoft Foundry** while also supporting direct ranked search via the `ISearcher` interface.

> **Requires Umbraco 17+ and an [Azure AI Search](https://azure.microsoft.com/en-us/products/ai-services/ai-search) instance.**

---

## Why this package?

Microsoft Foundry expects an Azure AI Search index with specific fields — `content`, `url`, and `title` — to ground AI responses. This package indexes Umbraco content directly into that schema, so you can connect your CMS to Foundry without any manual field mapping or data transformation.

At the same time, it maintains relevance tiers and a scoring profile for high-quality direct search results when querying outside of Foundry.

**Foundry uses:** `content`, `url`, `title`  
**Direct search uses:** `contentR1` (4×), `contentR2` (3×), `contentR3` (2×), `content` (1×)

---

## Quick Start

### 1. Install

```bash
dotnet add package Umbraco.Cms.Search.Core
dotnet add package GXE.Umbraco.AzureAISearch
```

### 2. Configure

Add to `appsettings.json`:

```json
{
  "AzureAISearch": {
    "Endpoint": "https://your-service.search.windows.net",
    "Key": "your-admin-api-key",
    "ExcludedContentTypes": [],
    "Environment": null
  }
}
```

| Setting | Description |
|---------|-------------|
| `Endpoint` | Your Azure AI Search service URL |
| `Key` | Admin API key |
| `BaseUrl` | *(Optional)* Fallback domain for URLs. If omitted, URLs are resolved automatically from Umbraco's domain configuration |
| `ExcludedContentTypes` | Array of content type aliases to skip during indexing |
| `Environment` | Optional prefix for index names (e.g. `"dev"` → `dev-publishedcontent`) |

### 3. Register

Create a composer:

```csharp
using Umbraco.AzureAISearch.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Search.Core.DependencyInjection;

public sealed class SearchComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddSearchCore();
        builder.AddUmbracoAzureAISearch(builder.Config);
    }
}
```

That's it. On startup, the index is created automatically. Content is indexed on publish.

---

## Index Schema

The generated Azure AI Search index contains:

| Field | Type | Purpose |
|-------|------|---------|
| `id` | String (Key) | Unique document identifier (`{guid}_{culture}_{segment}`) |
| `key` | String | Umbraco content GUID |
| `objectType` | String | Umbraco object type |
| `culture` | String | Culture code or `"inv"` |
| `segment` | String | Segment or `"def"` |
| `title` | String | Content node name |
| **`content`** | String | **All text combined, HTML-stripped — Foundry searches this** |
| `contentR1` | Collection | High-relevance text (titles, headings) |
| `contentR2` | Collection | Medium-relevance text |
| `contentR3` | Collection | Lower-relevance text |
| **`url`** | String | **Full absolute URL** |
| `accessKeys` | Collection | Content protection keys |

---

## Searching

Inject `IAzureAISearchSearcher` for direct search with full scoring:

```csharp
using Umbraco.AzureAISearch.Services.Searcher;

public class SiteSearchService(IAzureAISearchSearcher searcher)
{
    public async Task<SearchResult> SearchAsync(string query, string? culture = null)
        => await searcher.SearchAsync(
            indexAlias: Umbraco.Cms.Search.Core.Constants.IndexAliases.PublishedContent,
            query: query,
            culture: culture,
            skip: 0,
            take: 20);
}
```

The scoring profile automatically boosts `contentR1` (4×) over `contentR2` (3×) over `contentR3` (2×) over `content` (1×).

---

## Microsoft Foundry Integration

Once content is indexed, connect the Azure AI Search index in Foundry:

1. Go to your Foundry project → **Data sources**
2. Add an **Azure AI Search** connection pointing to your search service
3. Select the index (e.g. `publishedcontent` or `dev-publishedcontent`)
4. Foundry will use the `content` field for search and `url`/`title` for grounding responses

No additional mapping or field configuration is needed in Foundry.

---

## Server Role Awareness

On multi-server deployments, only the **primary** node manages the index. Subscriber nodes skip all write operations automatically.

---

## Releasing a New Version

1. Ensure all changes are committed and pushed to `main`
2. Tag the release with the version number:
   ```powershell
   git tag v1.2.0
   git push --tags
   ```
3. The GitHub Actions workflow automatically builds, tests, and publishes to NuGet.org via [Trusted Publishing](https://learn.microsoft.com/en-gb/nuget/nuget-org/trusted-publishing) (no API key required)

The version in the tag (e.g. `v1.2.0`) is used as the package version — no need to update `.csproj` manually.

---

## License

MIT
