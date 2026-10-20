# es-cli

[![build](https://github.com/izicoder/es-cli/actions/workflows/build.yml/badge.svg)](https://github.com/izicoder/es-cli/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/download)

Instant file search for Windows, straight from the command line — or from your AI agent.

Two small tools over the [voidtools Everything SDK](https://www.voidtools.com/support/everything/sdk/):

| Binary | What it is |
| --- | --- |
| `es-cli` | A CLI that queries the Everything index: paths, sizes, dates, JSON output, paging. |
| `es-mcp` | A [Model Context Protocol](https://modelcontextprotocol.io) server (stdio) that exposes the same search as a `search` tool for MCP clients. |

Because queries run against Everything's in-memory index, lookups across millions of files return in milliseconds — no directory walking, no indexing of your own.

## Requirements

- Windows x64
- [.NET 10 runtime](https://dotnet.microsoft.com/download) (or build self-contained, see below)
- [Everything](https://www.voidtools.com/) installed and **running in the background**
- `Everything64.dll`, the SDK shipped with Everything, reachable in one of these ways:
  1. `EVERYTHING_SDK_PATH` set to the DLL or to the folder containing it
  2. sitting next to `es-cli.exe` / `es-mcp.exe`
  3. the Everything install folder (found automatically, e.g. `C:\Program Files\Everything\`)

The DLL is deliberately **not** bundled here — grab it from your own Everything install.

## Build

```console
dotnet build
dotnet test
dotnet publish src/EsCli -c Release -o out
dotnet publish src/EsMcp -c Release -o out
```

`dotnet publish` produces framework-dependent single-file executables. For a machine without .NET installed, add `-p:SelfContained=true`.

## es-cli

```console
es-cli <query> [options]

  -n, --limit N        maximum results (1-1000, default 50)
  -o, --offset N       skip the first N results
      --regex          treat the query as a regular expression
      --case           match case
      --whole-word     match whole words only
      --path           match the full path, not just the file name
      --sort ORDER     name|name-desc|path|path-desc|size|size-desc|extension|
                       date-modified|date-modified-desc|date-created|date-created-desc|
                       date-accessed|date-accessed-desc|none  (default: path)
      --json           one JSON object per result (JSON Lines)
  -h, --help           help
  -v, --version        version
```

Query syntax is Everything's own: `ext:cs`, `folder:src`, `dm:today`, `size:>1gb`, wildcards, and so on.

```console
> es-cli *.sln --sort size-desc -n 3
# 3 shown / 128 total for "*.sln"
     9.1 KB  2026-01-14 15:02  E:\work\billing\Billing.sln  [sln]
     4.2 KB  2026-01-09 09:41  E:\work\web\Web.sln  [sln]
     3.7 KB  2026-01-08 11:20  E:\work\api\Api.sln  [sln]
# 125 more — raise --limit or use --offset

> es-cli report ext:pdf dm:thisweek --json | jq -r .path
E:\work\reports\q1.pdf
E:\work\reports\q2.pdf
```

`--json` emits one object per line:

```json
{"path":"E:\\work\\api\\Api.sln","name":"Api.sln","folder":false,"ext":"sln","size":3784,"modified":"2026-01-08T11:20:13.4412701+03:00"}
```

Exit codes: `0` success, `1` bad usage, `2` Everything SDK error (usually: Everything is not running).

## es-mcp

A stdio MCP server. Register it with any MCP client — for opencode, in `~/.config/opencode/opencode.json`:

```json
{
  "mcp": {
    "servers": {
      "everything": {
        "type": "local",
        "command": ["C:\\tools\\es-mcp\\es-mcp.exe"]
      }
    }
  }
}
```

(For Claude Desktop / Cursor style clients, the same idea: `"command": "C:\\tools\\es-mcp\\es-mcp.exe"`, no arguments.)

It exposes one tool:

### `search`

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `query` | string | — | Everything query (`report ext:pdf`, `folder:src`, `dm:today`, …) |
| `limit` | integer | 30 | 1-1000 results |
| `offset` | integer | 0 | results to skip, for paging |
| `regex` | boolean | false | query is a regular expression |
| `case_sensitive` | boolean | false | match case |
| `whole_word` | boolean | false | whole words only |
| `match_path` | boolean | false | match the full path, not just the name |
| `sort` | string | `path` | name, path, size, extension, date_modified, date_created, date_accessed (+ `_desc`), or a numeric SDK constant |
| `filter` | string | — | extra clause appended to the query, e.g. `ext:cs path:src` |

Results come back as a compact table:

```
20 total, showing 3
    1.2 MB  2026-02-03  C:\src\api\README.md
    4.4 KB  2026-02-03  C:\src\api\docs\README.md
   <dir>    2026-01-30  C:\src\api\docs
```

Agent-side, this replaces `dir`, `Get-ChildItem` and `where.exe`: one call answers "where is this file", "what is the biggest thing on this drive", "list every `.sln` under `E:\work`".

## Layout

```
src/EsSdk/            shared Everything SDK interop (native bindings, DLL resolution, search API)
src/EsCli/            command-line front end
src/EsMcp/            MCP stdio server
tests/EsCli.Tests/    xunit tests for argument parsing and formatting
```

`EsSdk` owns the only `DllImport` surface, so both front ends share one implementation and one error model.

## Notes and limits

- Everything must be running; the SDK talks to it over its IPC channel.
- The SDK keeps process-wide state, so `Everything.Search` serializes calls internally. Run one query at a time per process for maximum throughput.
- Folder results report no recursive size — Everything returns only the folder entry itself, so treat their `size` as meaningless rather than as disk usage.
- `date_created` / `date_accessed` sorting needs those properties enabled in Everything's options to be meaningful.

## License

[MIT](LICENSE) — the Everything SDK itself is © voidtools and distributed with Everything under its own terms.
