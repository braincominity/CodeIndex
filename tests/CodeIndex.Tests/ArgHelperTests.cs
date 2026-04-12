using CodeIndex.Cli;
using Xunit;

namespace CodeIndex.Tests;

public class ArgHelperTests
{
    [Fact]
    public void WantsHelp_BareLongFlag_ReturnsTrue()
    {
        Assert.True(ArgHelper.WantsHelp(new[] { "--help" }.AsSpan()));
    }

    [Fact]
    public void WantsHelp_BareShortFlag_ReturnsTrue()
    {
        Assert.True(ArgHelper.WantsHelp(new[] { "-h" }.AsSpan()));
    }

    [Fact]
    public void WantsHelp_AfterSubcommandArg_ReturnsTrue()
    {
        Assert.True(ArgHelper.WantsHelp(new[] { "foo", "--help" }.AsSpan()));
    }

    [Theory]
    [InlineData("--db")]
    [InlineData("--limit")]
    [InlineData("--top")]
    [InlineData("--lang")]
    [InlineData("--kind")]
    [InlineData("--path")]
    [InlineData("--exclude-path")]
    [InlineData("--since")]
    [InlineData("--start")]
    [InlineData("--end")]
    [InlineData("--before")]
    [InlineData("--after")]
    [InlineData("--snippet-lines")]
    [InlineData("--depth")]
    [InlineData("--completions")]
    public void WantsHelp_HelpTokenAsValueOfSingleValueFlag_ReturnsFalse(string flag)
    {
        Assert.False(ArgHelper.WantsHelp(new[] { "foo", flag, "-h" }.AsSpan()));
        Assert.False(ArgHelper.WantsHelp(new[] { "foo", flag, "--help" }.AsSpan()));
    }

    [Fact]
    public void WantsHelp_HelpAfterConsumedValue_StillReturnsTrue()
    {
        // `--limit 5` consumes "5", then --help is a real help request.
        Assert.True(ArgHelper.WantsHelp(new[] { "foo", "--limit", "5", "--help" }.AsSpan()));
    }

    [Fact]
    public void WantsHelp_NoHelpToken_ReturnsFalse()
    {
        Assert.False(ArgHelper.WantsHelp(new[] { "foo", "--limit", "5", "--path", "src" }.AsSpan()));
    }

    [Fact]
    public void WantsHelp_EmptyArgs_ReturnsFalse()
    {
        Assert.False(ArgHelper.WantsHelp(System.Array.Empty<string>().AsSpan()));
    }
}
