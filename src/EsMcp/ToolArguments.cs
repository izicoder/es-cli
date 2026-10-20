using System.Globalization;
using System.Text.Json;
using EsSdk;

namespace EsMcp;

/// <summary>A validated <c>tools/call</c> payload.</summary>
internal sealed record ToolRequest(string Query, SearchOptions Options);

/// <summary>
/// Turns the agent's JSON arguments into a <see cref="SearchOptions"/>. Reads leniently on purpose:
/// numbers may arrive as strings, booleans as "true"/"false", sorts as names or SDK constants.
/// </summary>
internal static class ToolArguments
{
    internal const int DefaultLimit = 30;
    internal const int MaxLimit = 1000;

    internal const string MissingQueryError =
        "error: 'query' is required - pass an Everything query, e.g. {\"query\":\"*.sln\"} or " +
        "{\"query\":\"report\",\"filter\":\"ext:pdf dm:thisweek\"}";

    /// <summary>Parse arguments. Returns <c>false</c> with an agent-readable <paramref name="error"/> when unusable.</summary>
    internal static bool TryParse(JsonElement arguments, out ToolRequest? request, out string? error)
    {
        request = null;
        error = null;

        var query = ReadString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            error = MissingQueryError;
            return false;
        }

        var options = new SearchOptions
        {
            Limit = (uint)Math.Clamp(ReadNumber(arguments, "limit", "max_results", "max") ?? DefaultLimit, 1, MaxLimit),
            Offset = (uint)Math.Max(0, ReadNumber(arguments, "offset") ?? 0),
            Regex = ReadBool(arguments, "regex"),
            MatchCase = ReadBool(arguments, "case_sensitive"),
            MatchWholeWord = ReadBool(arguments, "whole_word"),
            MatchPath = ReadBool(arguments, "match_path"),
            Sort = ReadSort(arguments),
            Filter = ReadString(arguments, "filter"),
        };

        request = new ToolRequest(query.Trim(), options);
        return true;
    }

    internal static string? ReadString(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    internal static bool ReadBool(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false,
        };
    }

    internal static double? ReadNumber(JsonElement arguments, params string[] names)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;

        foreach (var name in names)
        {
            if (!arguments.TryGetProperty(name, out var value)) continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return number;
        }

        return null;
    }

    internal static SortOrder ReadSort(JsonElement arguments)
    {
        // Accepts SDK numeric constants as well as names, with `sort_by` as an alias.
        var raw = ReadString(arguments, "sort") ?? ReadString(arguments, "sort_by");
        if (string.IsNullOrWhiteSpace(raw)) return SortOrder.Path;

        if (uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number <= (uint)SortOrder.DateAccessedDescending)
            return (SortOrder)number;

        var normalized = raw
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "name" => SortOrder.Name,
            "namedesc" or "namedescending" => SortOrder.NameDescending,
            "path" => SortOrder.Path,
            "pathdesc" or "pathdescending" => SortOrder.PathDescending,
            "size" => SortOrder.Size,
            "sizedesc" or "sizedescending" => SortOrder.SizeDescending,
            "extension" or "ext" => SortOrder.Extension,
            "extensiondesc" or "extdesc" => SortOrder.ExtensionDescending,
            "datemodified" or "modified" => SortOrder.DateModified,
            "datemodifieddesc" or "modifieddesc" => SortOrder.DateModifiedDescending,
            "datecreated" or "created" => SortOrder.DateCreated,
            "datecreateddesc" or "createddesc" => SortOrder.DateCreatedDescending,
            "dateaccessed" or "accessed" => SortOrder.DateAccessed,
            "dateaccesseddesc" or "accesseddesc" => SortOrder.DateAccessedDescending,
            "none" => SortOrder.None,
            _ => SortOrder.Path,
        };
    }
}
