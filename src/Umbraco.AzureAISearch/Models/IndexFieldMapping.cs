using Azure.Search.Documents.Indexes.Models;
using Umbraco.Cms.Search.Core.Models.Indexing;

namespace Umbraco.AzureAISearch.Models;

/// <summary>
/// Represents a mapping between an index field and its values with metadata for field creation.
/// </summary>
public sealed class IndexFieldMapping
{
    public required string FieldName { get; init; }
    public required object[] Values { get; init; }
    public required SearchFieldDataType FieldType { get; init; }
    public required bool IsCollection { get; init; }
    public required bool IsSortable { get; init; }
    public required bool IsSearchable { get; init; }
    public required bool IsFacetable { get; init; }
    public IndexField? SourceField { get; init; }
}
