using System.Text;
using NovaTerminal.Platform;

namespace NovaTerminal.Integration.Tests;

/// <summary>
/// Windows command-line quoting.
/// </summary>
/// <remarks>
/// Windows hands a process one string and leaves it to split, which is the opposite of the Unix
/// convention. Getting the rules wrong is not cosmetic: an argument containing a space silently
/// becomes two, and one ending in a backslash can escape the quote meant to contain it and swallow
/// the rest of the command line.
/// </remarks>
public sealed class CommandLineTests
{
    [Fact]
    public void SimpleArgumentsNeedNoQuoting()
    {
        Assert.Equal("pwsh.exe -NoLogo", CommandLine.Build("pwsh.exe", ["-NoLogo"]));
    }

    [Fact]
    public void ArgumentsWithSpacesAreQuoted()
    {
        Assert.Equal(
            @"""C:\Program Files\PowerShell\pwsh.exe""",
            CommandLine.Build(@"C:\Program Files\PowerShell\pwsh.exe", []));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has space", @"""has space""")]
    [InlineData("has\ttab", "\"has\ttab\"")]
    [InlineData(@"quote""inside", @"""quote\""inside""")]
    [InlineData("", @"""""")]
    public void ArgumentsAreQuotedOnlyWhenNecessary(string argument, string expected)
    {
        var builder = new StringBuilder();
        CommandLine.AppendArgument(builder, argument);

        Assert.Equal(expected, builder.ToString());
    }

    [Fact]
    public void ATrailingBackslashIsDoubledBeforeTheClosingQuote()
    {
        // Otherwise the backslash escapes the quote that was meant to close the argument, and
        // everything after it becomes part of the same argument.
        var builder = new StringBuilder();
        CommandLine.AppendArgument(builder, @"C:\path with space\");

        Assert.Equal(@"""C:\path with space\\""", builder.ToString());
    }

    [Fact]
    public void BackslashesNotAdjacentToAQuoteAreLeftAlone()
    {
        var builder = new StringBuilder();
        CommandLine.AppendArgument(builder, @"C:\some\path");

        Assert.Equal(@"C:\some\path", builder.ToString());
    }

    [Fact]
    public void BackslashesBeforeAnEmbeddedQuoteAreDoubled()
    {
        var builder = new StringBuilder();
        CommandLine.AppendArgument(builder, @"a\\""b");

        Assert.Equal(@"""a\\\\\""b""", builder.ToString());
    }

    [Fact]
    public void AnEmptyExecutableIsRejected()
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Build("  ", []));
    }
}
