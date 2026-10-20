// es-mcp — Model Context Protocol (stdio) server that exposes Everything search as a tool.
// Speaks newline-delimited JSON-RPC 2.0 on stdin/stdout; stdout carries protocol traffic only.
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

    /// <summary>Parsed once; the schema never changes at runtime.</summary>
    private static readonly JsonDocument SchemaDocument = JsonDocument.Parse(ToolCatalog.Schema);

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
                        instructions = ToolCatalog.Instructions,
                    });
                    break;

                case "ping":
                    Result(id, new { });
                    break;

                case "tools/list":
                    Result(id, new { tools = new[] { SchemaDocument.RootElement } });
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
            Error(id, -32602, $"missing params - expected {{name: \"{ToolCatalog.Name}\", arguments: {{query: \"*.sln\"}}}}");
            return;
        }

        var name = parameters.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (name is not (ToolCatalog.Name or ToolCatalog.LegacyName))
        {
            Error(id, -32602, $"unknown tool: {name} (available: {ToolCatalog.Name})");
            return;
        }

        var arguments = parameters.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
            ? a
            : default;

        if (!ToolArguments.TryParse(arguments, out var toolRequest, out var error))
        {
            ToolResult(id, (error!, true));
            return;
        }

        try
        {
            var results = Everything.Search(toolRequest!.Query, toolRequest.Options);
            ToolResult(id, (ResultFormatter.Format(toolRequest, results), false));
        }
        catch (EverythingException ex)
        {
            ToolResult(id, ($"error: {ex.Message}. Ask the user to start voidtools Everything, then retry.", true));
        }
        catch (ArgumentException ex)
        {
            ToolResult(id, ($"error: {ex.Message}", true));
        }
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
}
