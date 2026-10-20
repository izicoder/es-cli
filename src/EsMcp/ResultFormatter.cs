using System.Globalization;
using System.Text;
using EsSdk;

namespace EsMcp;

/// <summary>
/// Renders results for the agent. Every non-trivial outcome carries a next step: an empty result
/// says so plainly and suggests a different query instead of returning a bare "0".
/// </summary>
internal static class ResultFormatter
{
    internal static string Format(ToolRequest request, SearchResults results)
    {
        if (results.Entries.Count == 0)
        {
            return results.Total == 0
                ? NoMatches(request)
                : OffsetPastEnd(request, results.Total);
        }

        var text = new StringBuilder();
        text.AppendLine(Header(results, request.Options.Offset));

        foreach (var entry in results.Entries)
        {
            var size = entry.IsFolder ? "<dir>" : Display.HumanSize(entry.Size);
            var modified = entry.DateModified?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            text.AppendLine($"{size,9}  {modified,-10}  {entry.FullPath}");
        }

        var remaining = (long)results.Total - request.Options.Offset - results.Entries.Count;
        if (remaining > 0)
        {
            var next = request.Options.Offset + (uint)results.Entries.Count;
            text.AppendLine($"... {remaining} more of {results.Total} total (page with offset {next}, raise limit, or narrow the query)");
        }

        return text.ToString().TrimEnd();
    }

    private static string Header(SearchResults results, uint offset) =>
        results.Total == results.Entries.Count && offset == 0
            ? $"{results.Total} result{(results.Total == 1 ? string.Empty : "s")}"
            : offset > 0
                ? $"{results.Total} total, showing {results.Entries.Count} (offset {offset})"
                : $"{results.Total} total, showing {results.Entries.Count}";

    private static string NoMatches(ToolRequest request)
    {
        var description = string.IsNullOrWhiteSpace(request.Options.Filter)
            ? Quote(request.Query)
            : $"{Quote(request.Query)} + {Quote(request.Options.Filter)}";

        var text = new StringBuilder();
        text.AppendLine($"No files or folders match {description} - nothing was found for this query.");
        text.AppendLine("Try a different query:");
        text.AppendLine("- a shorter fragment or wildcard: 'report*' instead of 'quarterly_report_final.csv'");
        text.AppendLine("- drop narrowing terms: ext:, path:, dm: and size: are ANDed, so each one removes results");
        text.AppendLine("- search full paths: match_path=true, or wrap the fragment in path: like 'path:src\\api'");
        text.AppendLine("- broaden dates or sizes: 'dm:thisyear' instead of 'dm:today', 'size:>1mb' instead of 'size:>1gb'");

        return text.ToString().TrimEnd();
    }

    private static string OffsetPastEnd(ToolRequest request, uint total) =>
        $"No rows at offset {request.Options.Offset}: {Quote(request.Query)} has {total} match{(total == 1 ? string.Empty : "es")} in total. " +
        $"Retry with a smaller offset (0 for the first page), raise limit, or narrow the query.";

    private static string Quote(string value) => $"\"{value}\"";
}
