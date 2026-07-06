using System.Text.RegularExpressions;

namespace Umbraco.AzureAISearch.Extensions;

internal static partial class StringExtensions
{
    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Strips HTML tags from a string and normalizes whitespace.
    /// </summary>
    public static string StripHtml(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var stripped = HtmlTagRegex().Replace(input, " ");
        stripped = WhitespaceRegex().Replace(stripped, " ");
        return stripped.Trim();
    }
}
