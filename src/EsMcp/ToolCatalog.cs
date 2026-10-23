namespace EsMcp;

/// <summary>
/// Everything the calling agent sees before its first call: tool name, description, schema and
/// the server-level usage instructions returned from <c>initialize</c>.
/// Keep these texts front-loaded — clients truncate long descriptions.
/// </summary>
internal static class ToolCatalog
{
    /// <summary>Tool name. Explicit about the target (files) so agents prefer it over web search.</summary>
    internal const string Name = "search_files";

    /// <summary>Legacy name, still accepted on <c>tools/call</c> so older configs keep working.</summary>
    internal const string LegacyName = "search";

    internal const string Description =
        "Find any file or folder on this Windows machine instantly - full paths, sizes and dates from the " +
        "voidtools Everything index, with no directory walking. Prefer it over dir, Get-ChildItem, where.exe or " +
        "glob for locating files by name, extension, size or date, for anything outside the open project, and for " +
        "listing a directory tree. Query uses Everything syntax: plain words match file names ('report'), " +
        "ext:cs, path:src, dm:today, size:>1gb, wildcards ('report*.pdf'), quotes for spaces or literal paths " +
        "(path:\"C:\\work\\api\"), and regex:true for regular expressions. Space-separated terms are ANDed. " +
        "The reply header gives the total match count; when it shows fewer rows than the total, page with offset or " +
        "narrow the query. Read-only and safe to call repeatedly.";

    /// <summary>Short cheatsheet injected into the agent context by <c>initialize</c>.</summary>
    internal const string Instructions =
        """
        es-mcp - instant file search on Windows, backed by the voidtools Everything index.

        Tool: search_files(query, limit?, offset?, regex?, case_sensitive?, whole_word?, match_path?, sort?, filter?)
        Use it to locate a file or folder by name, extension, size or date, to list a directory tree, or to answer
        "where is X" anywhere on this machine - instead of dir, Get-ChildItem, where.exe or a filesystem walk.

        Query is Everything syntax:
          *.sln              file names ending in .sln
          report             names containing "report"
          ext:cs             by extension          path:src\api       by path fragment
          dm:today           modified today        size:>1gb          bigger than 1 GB
          "C:\work\api"      quoted literal path   regex:true         regular expression
        Space-separated terms are ANDed; use filter for the narrowing clause instead of one long query.
        folder: matches folder names only - use path:Downloads to search inside a folder. Named sorts
        are preferred (date_accessed can be slow); an unrecognised sort falls back to path.

        Reply format: "N total, showing M", then rows of "<size>  <date>  <full path>".
        If M is smaller than N, page with offset (+= limit) or tighten the query.

        If a call returns "Everything is not running", ask the user to start voidtools Everything.
        Read-only: call it as often as needed.
        """;

    /// <summary>JSON Schema advertised by <c>tools/list</c>.</summary>
    internal const string Schema =
        """
        {
          "name": "search_files",
          "title": "Search files and folders (Everything)",
          "description": "Find any file or folder on this Windows machine instantly - full paths, sizes and dates from the voidtools Everything index, with no directory walking. Prefer it over dir, Get-ChildItem, where.exe or glob for locating files by name, extension, size or date, for anything outside the open project, and for listing a directory tree. Query uses Everything syntax: plain words match file names ('report'), ext:cs, path:src, dm:today, size:>1gb, wildcards ('report*.pdf'), quotes for spaces or literal paths (path:\"C:\\work\\api\"), and regex:true for regular expressions. Space-separated terms are ANDed. The reply header gives the total match count; when it shows fewer rows than the total, page with offset or narrow the query. Read-only and safe to call repeatedly.",
          "annotations": {
            "title": "Search files and folders (Everything)",
            "readOnlyHint": true,
            "destructiveHint": false,
            "idempotentHint": true,
            "openWorldHint": false
          },
          "inputSchema": {
            "type": "object",
            "properties": {
              "query": {
                "type": "string",
                "description": "Everything query. Plain words match file names; ext:cs, path:src, dm:today, size:>1gb, wildcards and quoted paths all work. folder: matches folder names only - use path: to search inside a folder. Examples: '*.sln', 'report ext:pdf', 'path:\"C:\\work\\api\"'."
              },
              "limit": {
                "type": "integer",
                "minimum": 1,
                "maximum": 1000,
                "default": 30,
                "description": "Maximum rows to return (1-1000, default 30). The reply's total count tells you whether more exist."
              },
              "offset": {
                "type": "integer",
                "minimum": 0,
                "default": 0,
                "description": "Rows to skip, for paging: call again with offset += limit."
              },
              "regex": {
                "type": "boolean",
                "default": false,
                "description": "Treat the query as a regular expression (Everything's own engine)."
              },
              "case_sensitive": {
                "type": "boolean",
                "default": false,
                "description": "Match case. Off by default because Windows paths are case-insensitive."
              },
              "whole_word": {
                "type": "boolean",
                "default": false,
                "description": "Match whole words only, instead of substrings."
              },
              "match_path": {
                "type": "boolean",
                "default": false,
                "description": "Match against the full path instead of just the file name. Needed for fragments like 'src/api' outside of path:."
              },
              "sort": {
                "type": "string",
                "default": "path",
                "enum": [
                  "name", "name_desc",
                  "path", "path_desc",
                  "size", "size_desc",
                  "extension", "extension_desc",
                  "date_modified", "date_modified_desc",
                  "date_created", "date_created_desc",
                  "date_accessed", "date_accessed_desc",
                  "none"
                ],
                "description": "Result ordering. Use size_desc for 'largest files', date_modified_desc for 'most recently changed'. Named sorts are preferred; date_accessed can take seconds on large indexes, and an unrecognised value falls back to path. A numeric Everything sort constant (0-16) is also accepted; sort_by works as an alias."
              },
              "filter": {
                "type": "string",
                "description": "Extra Everything clause ANDed onto the query: 'ext:cs path:src', 'dm:thisweek'. Cleaner than one long query string."
              }
            },
            "required": ["query"],
            "examples": [
              { "query": "*.sln" },
              { "query": "report", "filter": "ext:pdf dm:thisweek" },
              { "query": "*", "sort": "size_desc", "limit": 10 },
              { "query": "config", "match_path": true, "filter": "path:src" }
            ]
          }
        }
        """;
}
