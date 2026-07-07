namespace Umbraco.AzureAISearch.Constants;

internal static class IndexConstants
{
    public const string ProviderIdentifier = "Umbraco.AzureAISearch";

    public static class Variation
    {
        public const string InvariantCulture = "inv";
        public const string DefaultSegment = "def";
    }

    public static class FieldNames
    {
        public const string Id = "id";
        public const string Key = "key";
        public const string ObjectType = "objectType";
        public const string Culture = "culture";
        public const string Segment = "segment";
        public const string AccessKeys = "accessKeys";
        public const string Title = "title";
        public const string Content = "content";
        public const string ContentR1 = "contentR1";
        public const string ContentR2 = "contentR2";
        public const string ContentR3 = "contentR3";
        public const string Url = "url";
        public const string PathIds = "pathIds";
    }

    public static class FieldTypePostfix
    {
        public const string Texts = "_texts";
        public const string TextsR1 = "_texts_r1";
        public const string TextsR2 = "_texts_r2";
        public const string TextsR3 = "_texts_r3";
        public const string Keywords = "_keywords";
        public const string Integers = "_integers";
        public const string Decimals = "_decimals";
        public const string DateTimeOffsets = "_datetimeoffsets";
        public const string Sortable = "_sort";
    }

    public static class ScoringProfiles
    {
        public const string RelevanceBoost = "relevanceBoost";
    }

    public static class SemanticConfigurations
    {
        public const string Default = "default";
    }

    /// <summary>
    /// System fields whose keyword values should NOT be included in the Foundry 'content' aggregate.
    /// </summary>
    public static readonly HashSet<string> SystemKeywordFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "contentTypeAlias",
        "templateAlias",
        "pathIds",
        "__NodeId",
        "__Path",
        "__NodeType",
        "__Published",
        "url",
    };

    /// <summary>
    /// Field name prefixes whose keyword values should NOT be included in the Foundry 'content' aggregate.
    /// These represent structural/system metadata rather than user-facing content.
    /// </summary>
    public static readonly string[] SystemKeywordFieldPrefixes =
    [
        "Umb_",
    ];
}
