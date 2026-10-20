using System.Text.Json;
using EsMcp;
using EsSdk;

namespace EsMcp.Tests;

public class ToolCatalogTests
{
    private static JsonElement Schema => JsonDocument.Parse(ToolCatalog.Schema).RootElement;

    [Fact]
    public void Tool_name_is_explicit_about_files()
    {
        Assert.Equal("search_files", ToolCatalog.Name);
        Assert.Equal("search", ToolCatalog.LegacyName);
        Assert.Equal(ToolCatalog.Name, Schema.GetProperty("name").GetString());
    }

    [Fact]
    public void Schema_is_valid_json_with_a_query_requirement()
    {
        var required = Schema.GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Contains("query", required);
        Assert.Equal(9, Schema.GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Count());
    }

    [Fact]
    public void Every_parameter_is_documented()
    {
        var properties = Schema.GetProperty("inputSchema").GetProperty("properties");

        foreach (var property in properties.EnumerateObject())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(property.Value.GetProperty("description").GetString()),
                $"{property.Name} has no description");
        }
    }

    [Fact]
    public void Tools_are_advertised_as_read_only()
    {
        var annotations = Schema.GetProperty("annotations");

        Assert.True(annotations.GetProperty("readOnlyHint").GetBoolean());
        Assert.False(annotations.GetProperty("openWorldHint").GetBoolean());
    }

    [Fact]
    public void Description_tells_agents_when_to_prefer_it()
    {
        Assert.Contains("Prefer it over dir", ToolCatalog.Description, StringComparison.Ordinal);
        Assert.Contains("Everything syntax", ToolCatalog.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Instructions_cover_the_essentials()
    {
        Assert.Contains(ToolCatalog.Name, ToolCatalog.Instructions, StringComparison.Ordinal);
        Assert.Contains("ext:cs", ToolCatalog.Instructions, StringComparison.Ordinal);
        Assert.Contains("filter", ToolCatalog.Instructions, StringComparison.Ordinal);
        Assert.Contains("offset", ToolCatalog.Instructions, StringComparison.Ordinal);
        Assert.Contains("not running", ToolCatalog.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void Instructions_stay_short_enough_for_a_context_window()
    {
        Assert.InRange(ToolCatalog.Instructions.Length, 200, 2000);
    }

    [Fact]
    public void Path_examples_use_single_backslashes()
    {
        const string example = @"path:""C:\work\api""";

        Assert.Contains(example, ToolCatalog.Description, StringComparison.Ordinal);
        Assert.Contains(example, Schema.GetProperty("inputSchema").GetProperty("properties").GetProperty("query").GetProperty("description").GetString()!, StringComparison.Ordinal);
    }
}

public class ToolArgumentsTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static ToolRequest Parse(string json)
    {
        Assert.True(ToolArguments.TryParse(Args(json), out var request, out var error), error);
        return request!;
    }

    [Fact]
    public void Applies_defaults()
    {
        var request = Parse("""{"query":"*.sln"}""");

        Assert.Equal("*.sln", request.Query);
        Assert.Equal(30u, request.Options.Limit);
        Assert.Equal(0u, request.Options.Offset);
        Assert.Equal(SortOrder.Path, request.Options.Sort);
        Assert.False(request.Options.Regex);
        Assert.False(request.Options.MatchCase);
        Assert.False(request.Options.MatchWholeWord);
        Assert.False(request.Options.MatchPath);
        Assert.Null(request.Options.Filter);
    }

    [Theory]
    [InlineData("""{"query":"   "}""")]
    [InlineData("""{"query":42}""")]
    [InlineData("""{"limit":5}""")]
    [InlineData("""{}""")]
    public void Rejects_a_missing_or_blank_query(string json)
    {
        Assert.False(ToolArguments.TryParse(Args(json), out var request, out var error));
        Assert.Null(request);
        Assert.Equal(ToolArguments.MissingQueryError, error);
    }

    [Fact]
    public void Missing_query_error_shows_a_usable_example()
    {
        Assert.Contains("\"query\":\"*.sln\"", ToolArguments.MissingQueryError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"query":"x","limit":0}""", 1u)]
    [InlineData("""{"query":"x","limit":5000}""", 1000u)]
    [InlineData("""{"query":"x","limit":"12"}""", 12u)]
    [InlineData("""{"query":"x","max_results":7}""", 7u)]
    [InlineData("""{"query":"x","max":3}""", 3u)]
    public void Reads_and_clamps_limit(string json, uint expected)
    {
        Assert.Equal(expected, Parse(json).Options.Limit);
    }

    [Theory]
    [InlineData("""{"query":"x","offset":"25"}""", 25u)]
    [InlineData("""{"query":"x","offset":-5}""", 0u)]
    public void Reads_offset_leniently(string json, uint expected)
    {
        Assert.Equal(expected, Parse(json).Options.Offset);
    }

    [Theory]
    [InlineData("""{"query":"x","regex":true}""", true)]
    [InlineData("""{"query":"x","regex":"true"}""", true)]
    [InlineData("""{"query":"x","regex":"false"}""", false)]
    [InlineData("""{"query":"x","regex":1}""", false)]
    public void Reads_booleans_leniently(string json, bool expected)
    {
        Assert.Equal(expected, Parse(json).Options.Regex);
    }

    [Theory]
    [InlineData("size_desc", SortOrder.SizeDescending)]
    [InlineData("date_modified_desc", SortOrder.DateModifiedDescending)]
    [InlineData("date-modified", SortOrder.DateModified)]
    [InlineData("NAME", SortOrder.Name)]
    [InlineData("ext", SortOrder.Extension)]
    [InlineData("6", SortOrder.SizeDescending)]
    [InlineData("nonsense", SortOrder.Path)]
    public void Reads_sort_names_and_constants(string sort, SortOrder expected)
    {
        Assert.Equal(expected, Parse($$"""{"query":"x","sort":"{{sort}}"}""").Options.Sort);
    }

    [Fact]
    public void Accepts_sort_by_alias_and_filter()
    {
        var request = Parse("""{"query":"report","sort_by":"size","filter":"ext:pdf dm:thisweek"}""");

        Assert.Equal(SortOrder.Size, request.Options.Sort);
        Assert.Equal("ext:pdf dm:thisweek", request.Options.Filter);
    }

    [Fact]
    public void Trims_the_query()
    {
        Assert.Equal("*.sln", Parse("""{"query":"  *.sln  "}""").Query);
    }
}

public class ResultFormatterTests
{
    private static ToolRequest Request(string query, string? filter = null, uint offset = 0, uint limit = 30) =>
        new(query, new SearchOptions { Filter = filter, Offset = offset, Limit = limit });

    private static FileEntry File(string path, long size = 1024) =>
        new(path, false, Path.GetExtension(path).TrimStart('.'), size, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local));

    [Fact]
    public void Single_result_is_singular()
    {
        var text = ResultFormatter.Format(Request("x"), new SearchResults(1, [File(@"C:\a\b.txt")]));

        Assert.StartsWith("1 result", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\a\b.txt", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncated_results_tell_the_agent_how_to_page()
    {
        var text = ResultFormatter.Format(Request("x", limit: 2), new SearchResults(10, [File(@"C:\a\1.txt"), File(@"C:\a\2.txt")]));

        Assert.Contains("10 total, showing 2", text, StringComparison.Ordinal);
        Assert.Contains("page with offset 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_search_says_nothing_was_found_and_what_to_try()
    {
        var text = ResultFormatter.Format(Request("quarterly_report_final.csv"), new SearchResults(0, []));

        Assert.Contains("No files or folders match \"quarterly_report_final.csv\"", text, StringComparison.Ordinal);
        Assert.Contains("nothing was found", text, StringComparison.Ordinal);
        Assert.Contains("match_path=true", text, StringComparison.Ordinal);
        Assert.Contains("shorter fragment", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_search_mentions_the_filter_when_one_was_used()
    {
        var text = ResultFormatter.Format(Request("report", "ext:pdf dm:today"), new SearchResults(0, []));

        Assert.Contains("\"report\" + \"ext:pdf dm:today\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_search_suggestions_stay_generic()
    {
        var text = ResultFormatter.Format(Request("some_user_file_2024.csv"), new SearchResults(0, []));

        Assert.Contains("\"some_user_file_2024.csv\"", text, StringComparison.Ordinal); // echoes what was asked
        Assert.Contains("instead of one exact file name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("quarterly_report_final.csv", text, StringComparison.Ordinal); // no stray sample name
    }

    [Fact]
    public void Offset_past_the_end_is_explained()
    {
        var text = ResultFormatter.Format(Request("*.sln", offset: 500), new SearchResults(12, []));

        Assert.Contains("No rows at offset 500", text, StringComparison.Ordinal);
        Assert.Contains("12 matches in total", text, StringComparison.Ordinal);
        Assert.Contains("smaller offset", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Folders_are_marked_as_directories()
    {
        var text = ResultFormatter.Format(Request("src"), new SearchResults(1, [new FileEntry(@"C:\a\src", true, "", 0, null)]));

        Assert.Contains("<dir>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncated_first_page_does_not_mention_offset_zero()
    {
        var text = ResultFormatter.Format(Request("x"), new SearchResults(5, [File(@"C:\a\1.txt")]));

        Assert.Contains("5 total, showing 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("offset 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Later_pages_show_the_offset()
    {
        var text = ResultFormatter.Format(Request("x", offset: 4), new SearchResults(5, [File(@"C:\a\5.txt")]));

        Assert.Contains("5 total, showing 1 (offset 4)", text, StringComparison.Ordinal);
    }
}
