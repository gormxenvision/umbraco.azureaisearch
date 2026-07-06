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
    }

    public static class ScoringProfiles
    {
        public const string RelevanceBoost = "relevanceBoost";
    }
}
