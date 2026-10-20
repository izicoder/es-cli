using EsCli;
using EsSdk;

namespace EsCli.Tests;

public class CommandLineTests
{
    [Fact]
    public void Applies_defaults()
    {
        var options = CommandLine.Parse(["*.sln"]);

        Assert.NotNull(options);
        Assert.Equal("*.sln", options.Query);
        Assert.Equal(50u, options.Limit);
        Assert.Equal(0u, options.Offset);
        Assert.Equal(SortOrder.Path, options.Sort);
        Assert.False(options.Json);
        Assert.False(options.Regex);
        Assert.False(options.MatchCase);
        Assert.False(options.MatchWholeWord);
        Assert.False(options.MatchPath);
    }

    [Fact]
    public void Rejoins_unquoted_multi_word_queries()
    {
        var options = CommandLine.Parse(["readme", "ext:md"]);

        Assert.Equal("readme ext:md", options!.Query);
    }

    [Theory]
    [InlineData("-n", "10", 10u)]
    [InlineData("--limit", "1000", 1000u)]
    public void Parses_limit(string flag, string value, uint expected)
    {
        Assert.Equal(expected, CommandLine.Parse(["x", flag, value])!.Limit);
    }

    [Fact]
    public void Parses_offset_and_flags()
    {
        var options = CommandLine.Parse(["x", "-o", "25", "--regex", "--case", "--whole-word", "--path", "--json"])!;

        Assert.Equal(25u, options.Offset);
        Assert.True(options.Regex);
        Assert.True(options.MatchCase);
        Assert.True(options.MatchWholeWord);
        Assert.True(options.MatchPath);
        Assert.True(options.Json);
    }

    [Theory]
    [InlineData("size", SortOrder.Size)]
    [InlineData("size-desc", SortOrder.SizeDescending)]
    [InlineData("date-modified-desc", SortOrder.DateModifiedDescending)]
    [InlineData("NAME", SortOrder.Name)]
    [InlineData("none", SortOrder.None)]
    public void Parses_sort_names(string name, SortOrder expected)
    {
        Assert.Equal(expected, CommandLine.Parse(["x", "--sort", name])!.Sort);
    }

    [Fact]
    public void Maps_to_search_options()
    {
        var searchOptions = CommandLine.Parse(["x", "-n", "7", "-o", "2", "--sort", "size"])!.ToSearchOptions();

        Assert.Equal(7u, searchOptions.Limit);
        Assert.Equal(2u, searchOptions.Offset);
        Assert.Equal(SortOrder.Size, searchOptions.Sort);
    }

    [Fact]
    public void Help_and_version_are_handled_without_a_query()
    {
        Assert.Null(CommandLine.Parse(["--help"]));
        Assert.Null(CommandLine.Parse(["-h"]));
        Assert.Null(CommandLine.Parse(["--version"]));
        Assert.Null(CommandLine.Parse(["-v"]));
    }

    [Fact]
    public void Rejects_unknown_option()
    {
        var error = Assert.Throws<CommandLineException>(() => CommandLine.Parse(["x", "--nope"]));
        Assert.Contains("--nope", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void Rejects_invalid_limit(string value)
    {
        Assert.Throws<CommandLineException>(() => CommandLine.Parse(["x", "-n", value]));
    }

    [Fact]
    public void Rejects_unknown_sort()
    {
        Assert.Throws<CommandLineException>(() => CommandLine.Parse(["x", "--sort", "sideways"]));
    }

    [Fact]
    public void Requires_a_query()
    {
        Assert.Throws<CommandLineException>(() => CommandLine.Parse(["-n", "5"]));
        Assert.Throws<CommandLineException>(() => CommandLine.Parse([]));
    }
}

public class DisplayTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(3_221_225_472, "3 GB")]
    public void Formats_byte_counts(long bytes, string expected)
    {
        Assert.Equal(expected, Display.HumanSize(bytes));
    }
}

public class FileEntryTests
{
    [Fact]
    public void Exposes_the_file_name()
    {
        var entry = new FileEntry(@"C:\work\report.pdf", false, "pdf", 1024, null);

        Assert.Equal("report.pdf", entry.Name);
    }
}
