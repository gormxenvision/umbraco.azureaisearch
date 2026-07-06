using Microsoft.Extensions.Options;
using Umbraco.AzureAISearch.Models;

namespace Umbraco.AzureAISearch.Services.IndexAliasResolver;

public interface IIndexAliasResolver
{
    string Resolve(string indexAlias);
}

internal sealed class IndexAliasResolver : IIndexAliasResolver
{
    private readonly string? _environment;

    public IndexAliasResolver(IOptions<AzureAISearchOptions> options)
    {
        _environment = options.Value.Environment;
    }

    public string Resolve(string indexAlias)
    {
        var name = indexAlias.ToLowerInvariant().Replace(".", "-");
        return string.IsNullOrEmpty(_environment)
            ? name
            : $"{_environment}-{name}";
    }
}
