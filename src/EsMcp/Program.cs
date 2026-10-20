// es-mcp — Model Context Protocol (stdio) server that exposes Everything search as a tool.
// Speaks newline-delimited JSON-RPC 2.0 on stdin/stdout; stdout carries protocol traffic only.
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EsSdk;

namespace EsMcp;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static int Main()
    {
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        Console.SetOut(output);

        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument request;
            try
            {
                request = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                Error(null, -32700, "parse error");
                continue;
            }

            using (request) Handle(request.RootElement);
        }

        return 0;
    }

    private static void Handle(JsonElement request)
    {
        var id = request.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
        var method = request.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

        try
        {
            switch (method)
            {
                case "initialize":
                    Result(id, new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "es-mcp", version = Version },
                    });
                    break;

                case "ping":
                    Result(id, new { });
                    break;

                case "tools/list":
                    Result(id, JsonSerializer.Deserialize<JsonElement>(ToolCatalog, Json));
                    break;

                case "tools/call":
                    CallTool(id, request);
                    break;

                case "resources/list":
                    Result(id, new { resources = Array.Empty<object>() });
                    break;

                case "prompts/list":
                    Result(id, new { prompts = Array.Empty<object>() });
                    break;

                case var _ when method is not null && method.StartsWith("notifications/", StringComparison.Ordinal):
                    break; // notifications never get a response

                case null:
                    break; // a response to something we never send

                default:
                    if (id is not null) Error(id, -32601, $"method not found: {method}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            if (id is not null) Error(id, -32602, $"invalid params: {ex.Message}");
        }
        catch (Exception ex)
        {
            if (id is not null) Error(id, -32603, $"internal error: {ex.Message}");
        }
    }

    private static void CallTool(JsonElement? id, JsonElement request)
    {
        if (!request.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            Error(id, -32602, "missing params");
            return;
        }

        var name = parameters.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (name != "search")
        {
            Error(id, -32602, $"unknown tool: {name}");
            return;
        }

        var arguments = parameters.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
            ? a
            : default;

        ToolResult(id, Search(arguments));
    }

    private static (string Text, bool IsError) Search(JsonElement arguments)
    {
        var query = ReadString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
            return ("error: missing required parameter 'query'", true);

        var options = new SearchOptions
        {
            Limit = (uint)Math.Clamp(ReadInt(arguments, "limit", "max_results", "max") ?? 30, 1, 1000),
            Offset = (uint)Math.Max(0, ReadInt(arguments, "offset") ?? 0),
            Regex = ReadBool(arguments, "regex"),
            MatchCase = ReadBool(arguments, "case_sensitive"),
            MatchWholeWord = ReadBool(arguments, "whole_word"),
            MatchPath = ReadBool(arguments, "match_path"),
            Sort = ReadSort(arguments),
            Filter = ReadString(arguments, "filter"),
        };

        try
        {
            var results = Everything.Search(query, options);
            return (Format(results, options.Offset), false);
        }
        catch (EverythingException ex)
        {
            return ($"error: {ex.Message} (is Everything running?)", true);
        }
        catch (ArgumentException ex)
        {
            return ($"error: {ex.Message}", true);
        }
    }

    private static string Format(SearchResults results, uint offset)
    {
        var text = new StringBuilder();

        if (results.Entries.Count == 0 && results.Total > 0 && offset > 0)
            return $"offset {offset} is past the last match ({results.Total} in total)";

        text.AppendLine(results.Total == results.Entries.Count && offset == 0
            ? $"{results.Total} result{(results.Total == 1 ? string.Empty : "s")}"
            : $"{results.Total} total, showing {results.Entries.Count}{(offset > 0 ? $" (offset {offset})" : string.Empty)}");

        foreach (var entry in results.Entries)
        {
            var size = entry.IsFolder ? "<dir>" : Display.HumanSize(entry.Size);
            var modified = entry.DateModified?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            text.AppendLine($"{size,9}  {modified,-10}  {entry.FullPath}");
        }

        var hidden = (long)results.Total - offset - results.Entries.Count;
        if (hidden > 0)
            text.AppendLine($"... {hidden} more (raise limit or use offset)");

        return text.ToString().TrimEnd();
    }

    private static string? ReadString(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static double? ReadInt(JsonElement arguments, params string[] names)
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

    private static SortOrder ReadSort(JsonElement arguments)
    {
        // Accepts the SDK numeric constants as well as names, with `sort_by` as an alias.
        var raw = ReadString(arguments, "sort") ?? ReadString(arguments, "sort_by");
        if (string.IsNullOrWhiteSpace(raw)) return SortOrder.Path;

        if (uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            if (number <= (uint)SortOrder.DateAccessedDescending) return (SortOrder)number;
        }

        var normalized = raw.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);

        return normalized.ToLowerInvariant() switch
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

    private static void ToolResult(JsonElement? id, (string Text, bool IsError) result) =>
        Result(id, new
        {
            content = new[] { new { type = "text", text = result.Text } },
            isError = result.IsError,
        });

    private static void Result(JsonElement? id, object result)
    {
        if (id is null) return; // notification: no response
        Console.Out.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, Json));
    }

    private static void Error(JsonElement? id, int code, string message) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = id ?? (object?)null,
            error = new { code, message },
        }, Json));

    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private const string ToolCatalog = """
        {
          "tools": [
            {
              "name": "search",
              "description": "Search files and folders instantly on Windows through voidtools Everything. Supports Everything query syntax: ext:exe, folder:, dm:today, path fragments, wildcards, regex. Use it to locate files by name, extension, size or date anywhere on the machine, and to list directory trees. Everything must be running in the background.",
              "inputSchema": {
                "type": "object",
                "properties": {
                  "query": {
                    "type": "string",
                    "description": "Everything query, e.g. report ext:pdf, path:src, dm:today, *.sln."
                  },
                  "limit": {
                    "type": "integer",
                    "minimum": 1,
                    "maximum": 1000,
                    "default": 30,
                    "description": "Maximum results to return."
                  },
                  "offset": {
                    "type": "integer",
                    "minimum": 0,
                    "default": 0,
                    "description": "Results to skip, for paging."
                  },
                  "regex": {
                    "type": "boolean",
                    "default": false,
                    "description": "Treat the query as a regular expression."
                  },
                  "case_sensitive": {
                    "type": "boolean",
                    "default": false,
                    "description": "Match case."
                  },
                  "whole_word": {
                    "type": "boolean",
                    "default": false,
                    "description": "Match whole words only."
                  },
                  "match_path": {
                    "type": "boolean",
                    "default": false,
                    "description": "Match against the full path, not just the file name."
                  },
                  "sort": {
                    "type": "string",
                    "default": "path",
                    "description": "name|name_desc|path|path_desc|size|size_desc|extension|extension_desc|date_modified|date_modified_desc|date_created|date_created_desc|date_accessed|date_accessed_desc|none, or a numeric Everything SDK sort constant (0-16). 'sort_by' is accepted as an alias."
                  },
                  "filter": {
                    "type": "string",
                    "description": "Extra Everything clause appended to the query, e.g. ext:cs path:src."
                  }
                },
                "required": ["query"]
              }
            }
          ]
        }
        """;
}
