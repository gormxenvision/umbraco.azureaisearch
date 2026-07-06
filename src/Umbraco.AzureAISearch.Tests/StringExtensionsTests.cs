using Umbraco.AzureAISearch.Extensions;
using Xunit;

namespace Umbraco.AzureAISearch.Tests;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("<p>Hello World</p>", "Hello World")]
    [InlineData("<h1>Title</h1><p>Body text</p>", "Title Body text")]
    [InlineData("<div class=\"test\">Content</div>", "Content")]
    [InlineData("No tags here", "No tags here")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("  <br/>  ", "")]
    [InlineData("<p>Multiple   spaces</p>", "Multiple spaces")]
    public void StripHtml_RemovesTags_And_NormalizesWhitespace(string? input, string expected)
    {
        var result = input.StripHtml();
        Assert.Equal(expected, result);
    }
}
