# cdidx

> **[日本語版はこちら / Japanese version](#cdidx日本語)**

[![Build and Test](https://github.com/Widthdom/CodeIndex/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/dotnet.yml)
[![CodeQL](https://github.com/Widthdom/CodeIndex/actions/workflows/codeql.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/codeql.yml)
[![Release](https://github.com/Widthdom/CodeIndex/actions/workflows/release.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/release.yml)

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/License-MIT-blue)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white)

**The AI-native local code index for terminal and MCP workflows.**

`cdidx` turns a repository into a reusable local search engine: full-text search over SQLite FTS5, symbol lookup, incremental refresh, human-readable CLI output, JSON for agents, and native MCP integration.

## Why cdidx

Most code search tools optimize for either desktop UI workflows or one-off text scanning in a shell. `cdidx` is built for a different loop: local repositories that need to be searched repeatedly by both humans and AI agents.

- `CLI-first` — designed for terminal workflows, scripts, and automation.
- `AI-native` — `--json` output and MCP structured results are built in, not bolted on.
- `Local-first` — SQLite database lives with the project in `.cdidx/`.
- `Incremental` — refresh only changed files with `--files` or `--commits`.

It is not an IDE replacement or desktop search app. It is a small local search runtime you can script, automate, and hand to AI tools.

Use `rg` when you want a zero-setup one-off scan. Use `cdidx` when the same repository will be searched again and again.

## cdidx vs rg

| | `rg` | `cdidx` |
|---|---|---|
| Best at | One-off text scans | Repeated local code search |
| Setup | None | One-time index build |
| Search model | Reads files every time | Queries a local SQLite FTS5 index |
| Output for automation | Plain text | Human-readable, JSON, and MCP |
| AI integration | Needs parsing | Structured by design |
| Updates after edits | Re-run search | Refresh only changed files |

## 30-Second Quick Start

`.NET 8 SDK required`

```bash
dotnet tool install -g cdidx
cdidx .
cdidx search "handleRequest"
```

That is the whole loop:

1. `cdidx .` builds or refreshes `.cdidx/codeindex.db`
2. `cdidx search ...` returns results from the local index
3. after edits, refresh with `cdidx . --files path/to/file.cs` or `cdidx . --commits HEAD`

## Prerequisites

.NET 8.0 SDK is required to build cdidx.

| OS | Install command |
|---|---|
| **Linux (Ubuntu/Debian)** | `sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0` |
| **Linux (Fedora)** | `sudo dnf install dotnet-sdk-8.0` |
| **macOS** | `brew install dotnet@8` |
| **Windows** | `winget install Microsoft.DotNet.SDK.8` |

Alternatively, download the installer from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0).

Verify:

```bash
dotnet --version   # should print 8.x.x
```

## Installation

### Option A: NuGet Global Tool (recommended)

```bash
dotnet tool install -g cdidx
```

That's it. `cdidx` is now available as a command.

#### Upgrade

If you already have cdidx installed, update to the latest version:

```bash
dotnet tool update -g cdidx
```

### Option B: Build from source

```bash
dotnet build src/CodeIndex/CodeIndex.csproj -c Release
dotnet publish src/CodeIndex/CodeIndex.csproj -c Release -o ./publish
```

Then add the binary to your PATH:

<details>
<summary><strong>Linux</strong></summary>

```bash
sudo cp ./publish/cdidx /usr/local/bin/cdidx
```
</details>

<details>
<summary><strong>macOS</strong></summary>

```bash
sudo cp ./publish/cdidx /usr/local/bin/cdidx
```

If `/usr/local/bin` is not in your PATH (Apple Silicon default shell):

```bash
echo 'export PATH="/usr/local/bin:$PATH"' >> ~/.zprofile
source ~/.zprofile
```
</details>

<details>
<summary><strong>Windows</strong></summary>

```powershell
# PowerShell (run as Administrator)
New-Item -ItemType Directory -Force -Path C:\Tools
Copy-Item .\publish\cdidx.exe C:\Tools\cdidx.exe

# Add to PATH permanently (current user)
$path = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($path -notlike '*C:\Tools*') {
    [Environment]::SetEnvironmentVariable('Path', "$path;C:\Tools", 'User')
}
```

Restart your terminal after adding to PATH.
</details>

### Verify

```bash
cdidx --version
```

## Quick Start

### Index a project

```bash
cdidx ./myproject
cdidx ./myproject --rebuild     # full rebuild from scratch
cdidx ./myproject --verbose     # show per-file details
```

By default, `cdidx index` stores the database in `<projectPath>/.cdidx/codeindex.db`, even if you run the command from another directory.

Default output:

```
⠹ Scanning...
  Found 42 files

Indexing...
  ████████████████████░░░░░░░░░░░░  67.0%  [28/42]

Done.

  Files   : 42
  Chunks  : 318
  Symbols : 156
  Skipped : 28 (unchanged)
  Elapsed : 00:00:02
```

With `--verbose`, each file also shows a status tag so you can see exactly what happened:

```
  [OK  ] src/app.cs (12 chunks, 5 symbols)
  [SKIP] src/utils.cs
  [DEL ] src/old.cs
  [ERR ] src/bad.cs: <message>
```

> `[OK  ]` = indexed successfully, `[SKIP]` = unchanged / skipped, `[DEL ]` = deleted from DB (file removed from disk), `[ERR ]` = failed (verbose mode includes stack trace)

This is useful for debugging indexing issues or verifying which files were actually processed.

### Search code

```bash
cdidx search "authenticate"              # full-text search
cdidx search "handleRequest" --lang go   # filter by language
cdidx search "TODO" --limit 50           # more results
cdidx search "auth*" --fts               # raw FTS5 syntax (prefix search)
```

Output:

```
src/Auth/Login.cs:15-30
  public bool Authenticate(string user, string pass)
  {
      var hash = ComputeHash(pass);
      return _store.Verify(user, hash);
  ...

src/Auth/TokenService.cs:42-58
  public string GenerateToken(User user)
  {
      var claims = BuildClaims(user);
      return _jwt.CreateToken(claims);
  ...

(2 results)
```

Human-readable search output is centered around the first matching line when possible, instead of always showing the start of the chunk.

Use `--json` for machine-readable output (AI agents):

```json
{"path":"src/Auth/Login.cs","start_line":15,"end_line":30,"content":"public bool Authenticate(...)...","lang":"csharp","score":12.5}
{"path":"src/Auth/TokenService.cs","lang":"csharp","chunk_start_line":1,"chunk_end_line":80,"snippet_start_line":40,"snippet_end_line":47,"snippet":"if (claims.Count == 0)\\n    throw new InvalidOperationException();\\nreturn GenerateToken(claims);","match_lines":[42,47],"highlights":[{"line":47,"text":"return GenerateToken(claims);","terms":["GenerateToken"]}],"context_before":2,"context_after":3,"score":9.8}
```

### Search symbols (functions, classes, etc.)

```bash
cdidx symbols UserService              # find by name
cdidx symbols --kind class             # all classes
cdidx symbols --kind function --lang python
```

Output:

```
class      UserService                              src/Services/UserService.cs:8-72
function   GetUserById                              src/Services/UserService.cs:24-41
function   CreateUser                               src/Services/UserService.cs:45-61
(3 symbols)
```

With `--json`, symbol results also include definition ranges, optional body ranges, signature text, container symbol, visibility, and return type when the language extractor can infer them:

```json
{"path":"src/Services/UserService.cs","lang":"csharp","kind":"function","name":"GetUserById","line":24,"start_line":24,"end_line":41,"body_start_line":26,"body_end_line":41,"signature":"public async Task<User> GetUserById(int id)","container_kind":"class","container_name":"UserService","visibility":"public","return_type":"Task<User>"}
```

`search`, `definition`, `references`, `callers`, `callees`, `symbols`, and `files` also share `--path <pattern>`, repeatable `--exclude-path <pattern>`, and `--exclude-tests` filters. Search results prefer source files over tests and docs, and `search` boosts files whose symbol names or paths match the query exactly.

`search --json` and MCP `search` return compact match-centered snippets instead of whole chunks. Each result includes `chunk_start_line`, `chunk_end_line`, `snippet_start_line`, `snippet_end_line`, `snippet`, `match_lines`, `highlights`, `context_before`, and `context_after`. Use `--snippet-lines <n>` to shrink or widen the excerpt window (default: 8, max: 20).

### Resolve a definition

```bash
cdidx definition ResolveGitCommonDir
cdidx definition ResolveGitCommonDir --path src/CodeIndex/Cli --exclude-tests
cdidx definition ResolveGitCommonDir --body --json
```

`definition` uses indexed symbol ranges plus chunk reconstruction to return the actual declaration text, and optional body content when the language extractor can infer a body range.

### Inspect one symbol in one round-trip

```bash
cdidx inspect ResolveGitCommonDir --exclude-tests
```

`inspect` bundles the primary definition, nearby symbols from the same file, references, callers, callees, and file metadata so AI clients can answer many symbol-oriented questions without chaining several separate commands.

### Find references, callers, and callees

```bash
cdidx references ResolveGitCommonDir --exclude-tests
cdidx callers ResolveGitCommonDir --exclude-tests --json
cdidx callees AddToGitExclude --exclude-tests
```

These commands use the indexed reference graph and are intended for languages where cdidx already extracts named symbols and call-like references: Python, JavaScript/TypeScript, C#, Go, Rust, Java, Kotlin, Ruby, C/C++, PHP, and Swift. For docs, config, markup, or other unsupported languages, fall back to `search`.

### Reconstruct a file excerpt

```bash
cdidx excerpt src/CodeIndex/Cli/GitHelper.cs --start 19 --end 28
cdidx excerpt src/CodeIndex/Cli/GitHelper.cs --start 19 --end 28 --before 3 --after 3 --json
```

### List files

```bash
cdidx files                            # all indexed files
cdidx files --lang csharp              # only C# files
cdidx files --path src/Services --exclude-path Migrations
```

Output:

```
csharp          120 lines  src/Services/UserService.cs
csharp           85 lines  src/Controllers/UserController.cs
csharp           42 lines  src/Models/User.cs
(3 files)
```

### Check status

```bash
cdidx status
```

Output:

```
Files   : 42
Chunks  : 318
Symbols : 156
Refs    : 912
Languages:
  csharp         28
  python         10
  javascript      4
```

### Map the repo before searching

```bash
cdidx map --path src/ --exclude-tests
cdidx map --path src/ --exclude-tests --json
```

`map` gives AI clients a fast repo overview with language breakdowns, module summaries, top files, largest files, symbol-rich files, reference-rich files, and likely entrypoints when heuristics can infer them.

`status --json` and `map --json` also expose freshness metadata such as `indexed_at`, `latest_modified`, `git_head`, and `git_is_dirty`. `files --json` includes per-file `checksum`, `modified`, and `indexed_at`, so AI clients can decide whether cached assumptions are still trustworthy.

## Options

| Option | Applies to | Description |
|---|---|---|
| `--db <path>` | All commands | Database file path. `index` defaults to `<projectPath>/.cdidx/codeindex.db`; query commands default to `.cdidx/codeindex.db` in the current directory. |
| `--json` | All commands | JSON output (for AI/machine use) |
| `--limit <n>` | Query commands | Max results (default: 20; `map` uses it per section) |
| `--lang <lang>` | Query commands | Filter by language |
| `--path <pattern>` | `search`, `definition`, `references`, `callers`, `callees`, `symbols`, `files`, `map`, `inspect` | Restrict results to paths containing this text |
| `--exclude-path <pattern>` | `search`, `definition`, `references`, `callers`, `callees`, `symbols`, `files`, `map`, `inspect` | Exclude paths containing this text (repeatable) |
| `--exclude-tests` | `search`, `definition`, `references`, `callers`, `callees`, `symbols`, `files`, `map`, `inspect` | Exclude likely test files and prefer production code |
| `--snippet-lines <n>` | `search` | Search snippet length for human-readable output and JSON/MCP snippets (default: 8, max: 20) |
| `--fts` | `search` | Use raw FTS5 query syntax instead of literal-safe quoting |
| `--kind <kind>` | `definition`, `symbols` | Filter by symbol kind (function/class/import/namespace) |
| `--body` | `definition`, `inspect` | Include reconstructed body content when the language extractor can infer the body range |
| `--start <line>` | `excerpt` | Start line for excerpt reconstruction |
| `--end <line>` | `excerpt` | End line for excerpt reconstruction (defaults to `--start`) |
| `--before <n>` | `excerpt` | Include extra context lines before the requested excerpt |
| `--after <n>` | `excerpt` | Include extra context lines after the requested excerpt |
| `--rebuild` | `index` | Delete existing DB and rebuild |
| `--verbose` | `index` | Show per-file status (`[OK  ]`/`[SKIP]`/`[DEL ]`/`[ERR ]`) |
| `--commits <id...>` | `index` | Update only files changed in specified commits |
| `--files <path...>` | `index` | Update only the specified files |

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Usage error (invalid arguments) |
| `2` | Not found (no search results, missing directory) |
| `3` | Database error |

## How it works

cdidx scans your project directory, splits each source file into overlapping chunks, and stores everything in a SQLite database with FTS5 full-text search. Incremental mode (default) first purges database entries for files that no longer exist on disk, then checks each file's last-modified timestamp against the database — only files whose timestamp exactly matches are skipped, and any difference (newer or older) triggers re-indexing. Newly appeared files are indexed as new entries. This means re-indexing after a branch switch only processes the files that actually differ.

## Git integration

`cdidx index` automatically adds `.cdidx/` to `.git/info/exclude`. You don't need to edit `.gitignore`.

`.git/info/exclude` is a standard Git mechanism that works just like `.gitignore`. Many tools use `.git/info/exclude` or store data inside `.git/` to avoid polluting `.gitignore` — git-lfs, git-secret, git-crypt, git-annex, Husky, pre-commit, JetBrains IDEs, VS Code (GitLens), Eclipse, etc.

## Git branch switching

The database reflects the working tree at the time of the last index. After switching branches, simply re-run `cdidx .` — files that no longer exist on disk are purged from the database, newly appeared files are indexed, and existing files are re-indexed only when their timestamp differs. The update is proportional to the number of changed files, not the total project size.

| Situation | What happens |
|---|---|
| File unchanged across branches | Skipped (instant) |
| File content changed | Re-indexed |
| File deleted after checkout | Purged from DB |
| File added after checkout | Indexed as new |

## Supported languages

| Language | Extensions | Symbols |
|---|---|:---:|
| Python | `.py` | yes |
| JavaScript | `.js`, `.jsx` | yes |
| TypeScript | `.ts`, `.tsx` | yes |
| C# | `.cs` | yes |
| Go | `.go` | yes |
| Rust | `.rs` | yes |
| Java | `.java` | yes |
| Kotlin | `.kt` | yes |
| Ruby | `.rb` | yes |
| C | `.c`, `.h` | yes |
| C++ | `.cpp`, `.cc`, `.cxx`, `.hpp`, `.hxx` | yes |
| PHP | `.php` | yes |
| Swift | `.swift` | yes |
| Shell | `.sh` | -- |
| SQL | `.sql` | -- |
| Markdown | `.md` | -- |
| YAML | `.yaml`, `.yml` | -- |
| JSON | `.json` | -- |
| TOML | `.toml` | -- |
| HTML | `.html` | -- |
| CSS | `.css`, `.scss` | -- |
| Vue | `.vue` | -- |
| Svelte | `.svelte` | -- |
| Terraform | `.tf` | -- |

All languages are fully searchable via FTS5. Languages with **Symbols = yes** also support structured queries by function/class/import name.

## Prerequisites: sqlite3

AI agents that query the database directly via SQL need the `sqlite3` CLI.

| OS | Status |
|---|---|
| **macOS** | Pre-installed |
| **Linux** | Usually pre-installed. If not: `sudo apt install sqlite3` |
| **Windows** | `winget install SQLite.SQLite` or `scoop install sqlite` |

## AI Integration

cdidx is designed as an AI-friendly code search tool. All query commands support `--json` for JSON lines output, making them easy to parse programmatically. `search`, `definition`, `references`, `callers`, `callees`, `symbols`, `files`, `map`, and `inspect` share path-aware narrowing via `--path`, repeatable `--exclude-path`, and `--exclude-tests`, so AI clients can cut noise before fetching excerpts. `inspect` plus MCP `analyze_symbol` bundle definition, nearby symbols, references, callers, callees, and file metadata into one round-trip, while `map` gives a repo-level overview for the first 30 seconds of investigation. `status --json` and `map --json` surface freshness metadata, and `files --json` exposes per-file checksums plus modified/indexed timestamps. `search --json` plus MCP `search` emit compact match-centered snippets with explicit line metadata instead of whole chunks, and `--snippet-lines` lets callers cap snippet size up front. Call graph data remains language-aware: unsupported languages should use `search` instead of assuming `references`/`callers`/`callees` will be populated. Opening an older database with a newer cdidx version will auto-add missing file/symbol columns and create newer reference tables when possible. If the DB cannot be migrated in place, read paths fall back to the legacy layout instead of crashing.

### Setup: Add to CLAUDE.md

To let AI agents use the generated index, place a `CLAUDE.md` in your project root:

````markdown
# Code Search Rules

This project uses **cdidx** for fast code search via a pre-built SQLite index (`.cdidx/codeindex.db`).
**Query this database** instead of using `find`, `grep`, or `ls -R`.

## Setup

First check if `cdidx` is available:

```bash
cdidx --version
```

**If not found**, install it (.NET 8+ SDK required):

```bash
dotnet --version   # must be 8.x or higher; if not, ask the user to install .NET 8+ SDK
dotnet tool install -g cdidx
```

**If already installed**, update to the latest version:

```bash
dotnet tool update -g cdidx
```

If update fails, the existing version still works — just continue with it. If install fails (no .NET SDK, no network), skip to the **"Direct SQL queries"** section below — you can query `.cdidx/codeindex.db` directly with `sqlite3`, provided the database was already built.

Before searching, update the index so results are accurate:

```bash
cdidx .   # incremental update (skips unchanged files)
```

## Keeping the index up to date (requires cdidx)

After editing files, update the database so search results stay accurate:

```bash
cdidx . --files path/to/changed_file.cs   # update specific files you modified
cdidx . --commits HEAD                     # update all files changed in the last commit
cdidx . --commits abc123                   # you can also pass a specific commit hash
cdidx .                                    # full incremental update (skips unchanged files)
```

**Rule: whenever you modify source files, run one of the above before your next search.**
If the checkout changed because of `git reset`, `git rebase`, `git commit --amend`, `git switch`, or `git merge`, prefer `cdidx .` so stale files are purged against the current worktree instead of only refreshing commit-local paths.

## Query strategy

- Start with `map` when you need a quick overview of languages, modules, and likely hot spots before issuing symbol or text queries.
- Check `status --json` when freshness matters. Use `indexed_at`, `latest_modified`, `git_head`, and `git_is_dirty` to decide whether you need to re-run `cdidx .` before trusting results.
- Use `inspect` when you already have a candidate symbol name and want bundled definition/caller/callee/reference context in one round-trip.
- Use `definition` when you need the declaration text for a named symbol, and add `--body` when the implementation body matters.
- Use `references`, `callers`, and `callees` for symbol-aware call graph questions in Python, JavaScript/TypeScript, C#, Go, Rust, Java, Kotlin, Ruby, C/C++, PHP, and Swift.
- Use `symbols` for named code entities in symbol-aware languages: Python, JavaScript/TypeScript, C#, Go, Rust, Java, Kotlin, Ruby, C/C++, PHP, and Swift.
- Use `search` for raw text, comments, strings, or languages without structured symbol extraction such as Shell, SQL, Markdown, YAML, JSON, TOML, HTML/CSS, Vue, Svelte, and Terraform.
- Add `--exclude-tests` unless you are explicitly investigating tests.
- Add `--path <text>` and repeatable `--exclude-path <text>` before broad searches so results stay inside the relevant module.
- Add `--snippet-lines <n>` to `search` when you need tighter JSON output before handing results to another model or tool.
- Use `files` to discover candidate paths, then `excerpt` to fetch only the needed lines instead of opening entire files.

## CLI (recommended if cdidx is available)

```bash
cdidx map --path src/ --exclude-tests --json
cdidx inspect "Authenticate" --exclude-tests
cdidx search "keyword" --path src/ --exclude-tests --snippet-lines 6
cdidx definition "ClassName" --path src/Services --body
cdidx callers "Authenticate" --lang csharp --exclude-tests
cdidx callees "HandleRequest" --path src/ --exclude-tests
cdidx symbols "ClassName" --lang csharp --exclude-tests
cdidx excerpt src/app.py --start 10 --end 20
cdidx files --lang python --path src/
cdidx status --json
```

## Direct SQL queries (fallback if cdidx is unavailable)

The queries below require `sqlite3`. If it is not installed, suggest the user install it:
- **macOS**: pre-installed
- **Linux**: `sudo apt install sqlite3`
- **Windows**: `winget install SQLite.SQLite` or `scoop install sqlite`

### Full-text search
```sql
SELECT f.path, c.start_line, c.content
FROM fts_chunks fc
JOIN chunks c ON c.id = fc.rowid
JOIN files f ON f.id = c.file_id
WHERE fts_chunks MATCH 'keyword'
LIMIT 20;
```

### Search by function/class name
```sql
SELECT f.path, s.name, s.line
FROM symbols s
JOIN files f ON f.id = s.file_id
WHERE s.kind = 'function' AND s.name LIKE '%keyword%';
```
````

### Incremental updates for CI / hooks

Instead of re-indexing the entire project, AI agents can update only the files that changed:

```bash
# Update only files changed in specific commits (e.g. in a post-merge hook)
cdidx ./myproject --commits abc123 def456

# Update only specific files (e.g. after saving a file in an editor hook)
cdidx ./myproject --files src/app.cs src/utils.cs
```

These options make it practical to keep the index up-to-date in real time, even on large codebases.

### MCP Server (for Claude Code, Cursor, Windsurf, etc.)

cdidx includes a built-in **MCP (Model Context Protocol) server**. MCP is a standard protocol that lets AI coding tools communicate with external programs. When you run `cdidx mcp`, cdidx starts listening on stdin/stdout — your AI tool sends search requests as JSON, and cdidx returns results instantly from the pre-built index.

Tool results include structured JSON in `structuredContent` plus a short text summary in `content`, so AI tools can parse typed data without scraping large text blocks.

```
┌──────────────┐  stdin (JSON-RPC)  ┌──────────┐
│  Claude Code │ ──────────────────→ │  cdidx   │
│  / Cursor    │ ←────────────────── │  mcp     │
│  / Windsurf  │  stdout (JSON-RPC) │  server  │
└──────────────┘                    └──────────┘
```

**Setup — add to your AI tool's config:**

Claude Code (`.claude/settings.json` or `.mcp.json`):

```json
{
  "mcpServers": {
    "cdidx": {
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

Cursor (`.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "cdidx": {
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

Windsurf (`.windsurf/mcp.json`):

```json
{
  "mcpServers": {
    "cdidx": {
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

GitHub Copilot (VS Code — `.vscode/mcp.json`):

```json
{
  "servers": {
    "cdidx": {
      "type": "stdio",
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

OpenAI Codex CLI (`codex.json` or `~/.codex/config.json`):

```json
{
  "mcpServers": {
    "cdidx": {
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

Once configured, the AI can directly call these tools:

| Tool | Description |
|---|---|
| `search` | Full-text search across code chunks |
| `definition` | Reconstruct a symbol declaration and optional body |
| `references` | Find indexed references for supported languages |
| `callers` | List callers for a named symbol in supported languages |
| `callees` | List callees for a named symbol in supported languages |
| `symbols` | Find functions, classes, interfaces, imports, and namespaces by name |
| `files` | List indexed files |
| `excerpt` | Reconstruct a specific line range from indexed chunks |
| `map` | Summarize languages, modules, hotspots, and likely entrypoints |
| `analyze_symbol` | Bundle definition, nearby symbols, references, callers, callees, and file metadata |
| `status` | Database statistics |
| `index` | Index or re-index a project directory |

No CLAUDE.md hacks or SQL templates needed — the AI interacts with cdidx natively.

### Why cdidx over grep/ripgrep for AI workflows?

| | `grep` / `rg` | `cdidx` |
|---|---|---|
| Output format | Plain text (needs parsing) | JSON lines (machine-ready) |
| Search speed on large repos | Scans every file each time | Pre-built FTS5 index |
| Symbol awareness | None | Functions, classes, imports |
| Incremental update | N/A | `--commits`, `--files` |

## More

- [Developer Guide](DEVELOPER_GUIDE.md) — Architecture, database schema, FTS5 internals, AI integration, design decisions
- [Self-Improvement Loop](SELF_IMPROVEMENT.md) — Ready-to-use operating contract for iterative AI-driven cdidx improvements

---

<a id="cdidx日本語"></a>
# cdidx（日本語）

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/License-MIT-blue)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white)

**ターミナルとMCPワークフローのための、AIネイティブなローカルコードインデックス。**

`cdidx` は、リポジトリを再利用可能なローカル検索エンジンに変えます。SQLite FTS5 による全文検索、シンボル検索、インクリメンタル更新、人間向けCLI出力、AI向けJSON出力、MCP連携をひとつにまとめています。

## なぜ cdidx なのか

多くのコード検索ツールは、デスクトップUI中心のワークフローか、シェルでの単発テキスト検索のどちらかに最適化されています。`cdidx` が狙っているのは別のループです。ローカルリポジトリを、人間とAIの両方が何度も検索する前提で設計しています。

- `CLI-first` — ターミナル、スクリプト、自動化向けに設計。
- `AI-native` — `--json` 出力と MCP の構造化結果を標準搭載。
- `Local-first` — SQLite DB はプロジェクト内の `.cdidx/` に配置。
- `Incremental` — `--files` と `--commits` で変更分だけ更新。

IDEの置き換えやデスクトップ検索アプリではありません。スクリプト可能で、自動化できて、AIツールにそのまま渡せる小さなローカル検索ランタイムです。

単発で文字列を掘りたいなら `rg`、同じリポジトリを人間とAIの両方が何度も検索するなら `cdidx` が向いています。

## cdidx と rg の違い

| | `rg` | `cdidx` |
|---|---|---|
| 得意な用途 | 単発のテキスト走査 | 繰り返し行うローカルコード検索 |
| 初期セットアップ | 不要 | 最初に一度インデックス作成 |
| 検索モデル | 毎回ファイルを読む | ローカルの SQLite FTS5 インデックスを検索 |
| 自動化向け出力 | プレーンテキスト | 人間向け出力、JSON、MCP |
| AI連携 | パースが必要 | 構造化前提 |
| 編集後の更新 | 再検索するだけ | 変更ファイルだけ更新できる |

## 30秒で試す

`.NET 8 SDK が必要`

```bash
dotnet tool install -g cdidx
cdidx .
cdidx search "handleRequest"
```

やることはこれだけです:

1. `cdidx .` で `.cdidx/codeindex.db` を作成または更新
2. `cdidx search ...` でローカルインデックスを検索
3. 編集後は `cdidx . --files path/to/file.cs` や `cdidx . --commits HEAD` で差分更新

## 前提条件

cdidxのビルドには .NET 8.0 SDK が必要です。

| OS | インストールコマンド |
|---|---|
| **Linux (Ubuntu/Debian)** | `sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0` |
| **Linux (Fedora)** | `sudo dnf install dotnet-sdk-8.0` |
| **macOS** | `brew install dotnet@8` |
| **Windows** | `winget install Microsoft.DotNet.SDK.8` |

または [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) からインストーラーをダウンロードしてください。

確認:

```bash
dotnet --version   # 8.x.x と表示されること
```

## インストール

### 方法A: NuGet グローバルツール（推奨）

```bash
dotnet tool install -g cdidx
```

これだけです。`cdidx` コマンドがすぐ使えます。

#### アップグレード

すでにインストール済みの場合、最新版に更新できます:

```bash
dotnet tool update -g cdidx
```

### 方法B: ソースからビルド

```bash
dotnet build src/CodeIndex/CodeIndex.csproj -c Release
dotnet publish src/CodeIndex/CodeIndex.csproj -c Release -o ./publish
```

ビルド後、バイナリをPATHに追加します:

<details>
<summary><strong>Linux</strong></summary>

```bash
sudo cp ./publish/cdidx /usr/local/bin/cdidx
```
</details>

<details>
<summary><strong>macOS</strong></summary>

```bash
sudo cp ./publish/cdidx /usr/local/bin/cdidx
```

`/usr/local/bin` がPATHに含まれていない場合（Apple Siliconのデフォルトシェル）:

```bash
echo 'export PATH="/usr/local/bin:$PATH"' >> ~/.zprofile
source ~/.zprofile
```
</details>

<details>
<summary><strong>Windows</strong></summary>

```powershell
# PowerShell（管理者として実行）
New-Item -ItemType Directory -Force -Path C:\Tools
Copy-Item .\publish\cdidx.exe C:\Tools\cdidx.exe

# PATHに永続的に追加（現在のユーザー）
$path = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($path -notlike '*C:\Tools*') {
    [Environment]::SetEnvironmentVariable('Path', "$path;C:\Tools", 'User')
}
```

PATH追加後はターミナルを再起動してください。
</details>

### 確認

```bash
cdidx --version
```

## クイックスタート

### プロジェクトをインデックス

```bash
cdidx ./myproject
cdidx ./myproject --rebuild     # 完全再構築
cdidx ./myproject --verbose     # ファイルごとの詳細表示
```

`cdidx index` は、別ディレクトリから実行しても、デフォルトでは `<projectPath>/.cdidx/codeindex.db` にDBを保存します。

デフォルト出力:

```
⠹ Scanning...
  Found 42 files

Indexing...
  ████████████████████░░░░░░░░░░░░  67.0%  [28/42]

Done.

  Files   : 42
  Chunks  : 318
  Symbols : 156
  Skipped : 28 (unchanged)
  Elapsed : 00:00:02
```

`--verbose` を付けると、各ファイルにステータスタグも表示され、何が起きたか一目でわかります:

```
  [OK  ] src/app.cs (12 chunks, 5 symbols)
  [SKIP] src/utils.cs
  [DEL ] src/old.cs
  [ERR ] src/bad.cs: <message>
```

> `[OK  ]` = インデックス成功、`[SKIP]` = 未変更・スキップ、`[DEL ]` = DBから削除（ディスク上のファイルが消えた）、`[ERR ]` = 失敗（verboseではスタックトレースも表示）

インデックスの問題をデバッグしたり、どのファイルが実際に処理されたかを確認するのに便利です。

### コード検索

```bash
cdidx search "authenticate"              # 全文検索
cdidx search "handleRequest" --lang go   # 言語でフィルタ
cdidx search "TODO" --limit 50           # 結果数を増やす
cdidx search "auth*" --fts               # 生のFTS5構文（前方一致検索）
```

出力:

```
src/Auth/Login.cs:15-30
  public bool Authenticate(string user, string pass)
  {
      var hash = ComputeHash(pass);
      return _store.Verify(user, hash);
  ...

src/Auth/TokenService.cs:42-58
  public string GenerateToken(User user)
  {
      var claims = BuildClaims(user);
      return _jwt.CreateToken(claims);
  ...

(2 results)
```

人間向けの検索出力は、可能な限り最初の一致行を中心にスニペットを表示し、常にチャンク先頭だけを出すことはありません。

`--json` でAI/機械向け出力:

```json
{"path":"src/Auth/Login.cs","start_line":15,"end_line":30,"content":"public bool Authenticate(...)...","lang":"csharp","score":12.5}
{"path":"src/Auth/TokenService.cs","start_line":42,"end_line":58,"content":"public string GenerateToken(...)...","lang":"csharp","score":9.8}
```

### シンボル検索（関数、クラスなど）

```bash
cdidx symbols UserService              # 名前で検索
cdidx symbols --kind class             # すべてのクラス
cdidx symbols --kind function --lang python
```

出力:

```
class      UserService                              src/Services/UserService.cs:8-72
function   GetUserById                              src/Services/UserService.cs:24-41
function   CreateUser                               src/Services/UserService.cs:45-61
(3 symbols)
```

`--json` を使うと、シンボル結果には定義範囲、判定できる場合の本体範囲、シグネチャ文字列、親シンボル、可視性、戻り値型も含まれます。

```json
{"path":"src/Services/UserService.cs","lang":"csharp","kind":"function","name":"GetUserById","line":24,"start_line":24,"end_line":41,"body_start_line":26,"body_end_line":41,"signature":"public async Task<User> GetUserById(int id)","container_kind":"class","container_name":"UserService","visibility":"public","return_type":"Task<User>"}
```

`search`、`definition`、`references`、`callers`、`callees`、`symbols`、`files` は共通で `--path <pattern>`、繰り返し指定できる `--exclude-path <pattern>`、`--exclude-tests` に対応しています。検索結果は tests や docs より source を優先し、`search` はシンボル名やパスがクエリと正確に一致するファイルを上に出します。

`search --json` と MCP の `search` は、チャンク全文ではなく一致中心の軽量スニペットを返します。各結果には `chunk_start_line`、`chunk_end_line`、`snippet_start_line`、`snippet_end_line`、`snippet`、`match_lines`、`highlights`、`context_before`、`context_after` が含まれます。抜粋の長さは `--snippet-lines <n>` で調整できます（デフォルト: 8、最大: 20）。

### 定義を引く

```bash
cdidx definition ResolveGitCommonDir
cdidx definition ResolveGitCommonDir --path src/CodeIndex/Cli --exclude-tests
cdidx definition ResolveGitCommonDir --body --json
```

`definition` は、インデックス済みシンボル範囲とチャンク再構成を使って実際の宣言テキストを返します。言語抽出器が本体範囲を推論できる場合は、`--body` で本体内容も返します。

### 1往復でシンボルを精査する

```bash
cdidx inspect ResolveGitCommonDir --exclude-tests
```

`inspect` は、主定義、同一ファイル内の近傍シンボル、参照、caller、callee、ファイルメタデータをまとめて返すため、AIクライアントが複数コマンドを連鎖させずにシンボル調査を進められます。

### 参照、callers、callees を調べる

```bash
cdidx references ResolveGitCommonDir --exclude-tests
cdidx callers ResolveGitCommonDir --exclude-tests --json
cdidx callees AddToGitExclude --exclude-tests
```

これらのコマンドはインデックス済み参照グラフを使います。対象は、cdidx が名前付きシンボルと call 相当の参照を抽出している言語、つまり Python、JavaScript/TypeScript、C#、Go、Rust、Java、Kotlin、Ruby、C/C++、PHP、Swift です。ドキュメント、設定ファイル、マークアップなどの未対応言語では `search` に戻してください。

### ファイル抜粋を再構成する

```bash
cdidx excerpt src/CodeIndex/Cli/GitHelper.cs --start 19 --end 28
cdidx excerpt src/CodeIndex/Cli/GitHelper.cs --start 19 --end 28 --before 3 --after 3 --json
```

### ファイル一覧

```bash
cdidx files                            # 全インデックス済みファイル
cdidx files --lang csharp              # C#ファイルのみ
cdidx files --path src/Services --exclude-path Migrations
```

出力:

```
csharp          120 lines  src/Services/UserService.cs
csharp           85 lines  src/Controllers/UserController.cs
csharp           42 lines  src/Models/User.cs
(3 files)
```

### 状態確認

```bash
cdidx status
```

出力:

```
Files   : 42
Chunks  : 318
Symbols : 156
Refs    : 912
Languages:
  csharp         28
  python         10
  javascript      4
```

### 検索前にリポジトリ全体を俯瞰する

```bash
cdidx map --path src/ --exclude-tests
cdidx map --path src/ --exclude-tests --json
```

`map` は、言語別内訳、モジュール要約、主要ファイル、巨大ファイル、シンボル密度の高いファイル、参照密度の高いファイル、推定できる場合のエントリポイントをまとめて返し、AIクライアントが最初の30秒で地図を作れるようにします。

さらに `status --json` と `map --json` は `indexed_at`、`latest_modified`、`git_head`、`git_is_dirty` のような鮮度メタデータを返します。`files --json` にはファイル単位の `checksum`、`modified`、`indexed_at` が含まれるため、AIクライアントは仮説が古くなっていないか判断できます。

## オプション一覧

| オプション | 対象 | 説明 |
|---|---|---|
| `--db <path>` | 全コマンド | DBファイルパス。`index` のデフォルトは `<projectPath>/.cdidx/codeindex.db`、クエリ系コマンドのデフォルトはカレントディレクトリの `.cdidx/codeindex.db`。 |
| `--json` | 全コマンド | JSON出力（AI/機械向け） |
| `--limit <n>` | クエリ系 | 最大結果数（デフォルト: 20。`map` では各セクションごとの件数） |
| `--lang <lang>` | クエリ系 | 言語でフィルタ |
| `--path <pattern>` | `search`, `definition`, `references`, `callers`, `callees`, `symbols`, `files`, `map`, `inspect` | 指定文字列を含むパスに結果を絞る |
| `--exclude-path <pattern>` | `search`, `definition`, `references`, `callers`, `callees`, `symbols`, `files`, `map`, `inspect` | 指定文字列を含むパスを除外（繰り返し指定可） |
| `--exclude-tests` | `search`, `definition`, `references`, `callers`, `callees`, `symbols`, `files`, `map`, `inspect` | テストらしいパスを除外し、本番コードを優先 |
| `--snippet-lines <n>` | `search` | 人間向け出力と JSON/MCP スニペットの抜粋行数（デフォルト: 8、最大: 20） |
| `--fts` | `search` | リテラル安全な引用ではなく生のFTS5クエリ構文を使う |
| `--kind <kind>` | `definition`, `symbols` | シンボル種別でフィルタ（function/class/import/namespace） |
| `--body` | `definition`, `inspect` | 言語抽出器が本体範囲を推論できる場合に本体内容も含める |
| `--start <line>` | `excerpt` | 抜粋再構成の開始行 |
| `--end <line>` | `excerpt` | 抜粋再構成の終了行（省略時は `--start` と同じ） |
| `--before <n>` | `excerpt` | 指定範囲の前に追加する文脈行数 |
| `--after <n>` | `excerpt` | 指定範囲の後に追加する文脈行数 |
| `--rebuild` | `index` | 既存DBを削除して再構築 |
| `--verbose` | `index` | ファイルごとのステータス表示（`[OK  ]`/`[SKIP]`/`[DEL ]`/`[ERR ]`） |
| `--commits <id...>` | `index` | 指定コミットの変更ファイルのみ更新 |
| `--files <path...>` | `index` | 指定ファイルのみ更新 |

### 終了コード

| コード | 意味 |
|---|---|
| `0` | 成功 |
| `1` | 引数エラー |
| `2` | 未検出（検索結果なし、ディレクトリ不在） |
| `3` | データベースエラー |

## 動作の仕組み

cdidxはプロジェクトディレクトリを走査し、各ソースファイルを重複を持つチャンクに分割し、FTS5全文検索付きのSQLiteデータベースに格納します。インクリメンタルモード（デフォルト）では各ファイルの最終更新タイムスタンプをDB内の値と比較し、完全一致するファイルのみスキップします。タイムスタンプが異なれば（新しくても古くても）再インデックスされるため、ブランチ切り替え後も正確にインデックスが更新されます。

## Git連携

`cdidx index` を実行すると、`.cdidx/` が自動で `.git/info/exclude` に追加されます。`.gitignore` を編集する必要はありません。

`.git/info/exclude` は `.gitignore` と同じ効果を持つ Git 標準の仕組みです。`.gitignore` を汚さないよう `.git/info/exclude` や `.git/` 配下を利用するツールは多数あります — git-lfs、git-secret、git-crypt、git-annex、Husky、pre-commit、JetBrains IDE、VS Code (GitLens)、Eclipse など。

## Gitブランチ切り替え

データベースはインデックス実行時のワーキングツリーを反映します。ブランチ切り替え後は `cdidx .` を再実行してください。ディスク上から消えたファイルはDBからパージされ、新たに現れたファイルはインデックスに追加され、既存ファイルはタイムスタンプが異なる場合のみ再インデックスされます。更新量はプロジェクト全体のサイズではなく変更ファイル数に比例します。

| 状況 | 動作 |
|---|---|
| ブランチ間でファイル未変更 | スキップ（即時） |
| ファイル内容が変更 | 再インデックス |
| checkout後にファイル削除 | DBからパージ |
| checkout後にファイル追加 | 新規インデックス |

## 対応言語

| 言語 | 拡張子 | シンボル |
|---|---|:---:|
| Python | `.py` | yes |
| JavaScript | `.js`, `.jsx` | yes |
| TypeScript | `.ts`, `.tsx` | yes |
| C# | `.cs` | yes |
| Go | `.go` | yes |
| Rust | `.rs` | yes |
| Java | `.java` | yes |
| Kotlin | `.kt` | yes |
| Ruby | `.rb` | yes |
| C | `.c`, `.h` | yes |
| C++ | `.cpp`, `.cc`, `.cxx`, `.hpp`, `.hxx` | yes |
| PHP | `.php` | yes |
| Swift | `.swift` | yes |
| Shell | `.sh` | -- |
| SQL | `.sql` | -- |
| Markdown | `.md` | -- |
| YAML | `.yaml`, `.yml` | -- |
| JSON | `.json` | -- |
| TOML | `.toml` | -- |
| HTML | `.html` | -- |
| CSS | `.css`, `.scss` | -- |
| Vue | `.vue` | -- |
| Svelte | `.svelte` | -- |
| Terraform | `.tf` | -- |

全言語がFTS5による全文検索に対応。**シンボル = yes** の言語は関数・クラス・インポート名での構造化検索にも対応しています。

## 前提条件: sqlite3

AIエージェントがDBを直接SQL検索する場合、`sqlite3` CLIが必要です。

| OS | 状況 |
|---|---|
| **macOS** | プリインストール済み |
| **Linux** | 通常プリインストール済み。未導入時: `sudo apt install sqlite3` |
| **Windows** | `winget install SQLite.SQLite` または `scoop install sqlite` |

## AIとの連携

cdidxはAI対応のコード検索ツールとして設計されています。すべてのクエリコマンドは `--json` でJSONライン出力に対応し、プログラムからのパースが容易です。`search`、`definition`、`references`、`callers`、`callees`、`symbols`、`files`、`map`、`inspect` は `--path`、繰り返し指定できる `--exclude-path`、`--exclude-tests` で共通に絞り込みできるため、AIクライアントは抜粋取得前にノイズを減らせます。`inspect` と MCP の `analyze_symbol` は、定義、近傍シンボル、参照、caller、callee、ファイルメタデータを1往復でまとめて返し、`map` は調査開始直後の俯瞰を返します。`status --json` と `map --json` は鮮度メタデータを返し、`files --json` はファイルごとの checksum と modified/indexed timestamp を返します。`search --json` と MCP の `search` は、チャンク全文ではなく一致中心の軽量スニペットと明示的な行メタデータを返し、`--snippet-lines` でそのサイズを先に制限できます。call graph 系のデータは言語差分を考慮しており、未対応言語では `references` / `callers` / `callees` が空でも正常です。その場合は `search` を優先してください。古いDBを新しいcdidxで開いた場合も、可能なら不足する file/symbol 列を自動追加し、新しい参照テーブルも作成します。DBをその場で移行できない場合でも、読み取り経路は旧レイアウトへフォールバックし、クラッシュしません。

### セットアップ: CLAUDE.mdに追加

AIエージェントにインデックスを活用させるには、プロジェクトルートに `CLAUDE.md` を配置してください:

````markdown
# コードベース検索ルール

このプロジェクトは **cdidx** を使い、事前構築済みSQLiteインデックス（`.cdidx/codeindex.db`）で高速コード検索を行います。
コードを検索する際は `find`, `grep`, `ls -R` ではなく**このデータベースを検索**してください。

## セットアップ

まず `cdidx` が利用可能か確認してください:

```bash
cdidx --version
```

**見つからない場合**、インストールしてください（.NET 8+ SDK必須）:

```bash
dotnet --version   # 8.x以上であること。そうでなければユーザーに.NET 8+ SDKのインストールを依頼
dotnet tool install -g cdidx
```

**すでにインストール済みの場合**、最新版に更新してください:

```bash
dotnet tool update -g cdidx
```

更新に失敗しても既存バージョンはそのまま使えます。インストール自体に失敗した場合（.NET SDKがない、ネットワーク不通等）は、データベースが構築済みであれば下記の **「直接SQLクエリ」** セクションで `sqlite3` から `.cdidx/codeindex.db` を直接検索できます。

検索を始める前に、インデックスを最新化してください:

```bash
cdidx .   # インクリメンタル更新（未変更ファイルはスキップ）
```

## インデックスの最新化（cdidxが必要）

ファイルを編集したら、検索結果を正確に保つためにデータベースを更新してください:

```bash
cdidx . --files path/to/changed_file.cs   # 変更したファイルだけ更新
cdidx . --commits HEAD                     # 直前のコミットで変更されたファイルを更新
cdidx . --commits abc123                   # 特定のコミットハッシュも指定可能
cdidx .                                    # フルインクリメンタル更新（未変更ファイルはスキップ）
```

**ルール: ソースファイルを修正したら、次の検索の前に上記のいずれかを実行すること。**
`git reset`、`git rebase`、`git commit --amend`、`git switch`、`git merge` で checkout 自体が変わった後は、コミット単位の更新だけでなく stale file の掃除も必要になるため、`cdidx .` を優先してください。

## クエリ戦略

- まず全体像が欲しいときは `map` から始めて、言語、モジュール、主要ファイル、ホットスポットを把握する。
- 鮮度が重要な場合は `status --json` を先に見て、`indexed_at`、`latest_modified`、`git_head`、`git_is_dirty` を確認してから検索結果を信用するか判断する。
- 候補シンボル名が決まっていて、定義・caller・callee・参照をまとめて欲しいときは `inspect` を使う。
- 名前付きシンボルの宣言を取りたいときは `definition` を使い、本体も必要なら `--body` を付ける。
- Python、JavaScript/TypeScript、C#、Go、Rust、Java、Kotlin、Ruby、C/C++、PHP、Swift の call graph 系調査では `references`、`callers`、`callees` を使う。
- Python、JavaScript/TypeScript、C#、Go、Rust、Java、Kotlin、Ruby、C/C++、PHP、Swift のようなシンボル抽出対応言語では、名前ベースの調査に `symbols` を使う。
- Shell、SQL、Markdown、YAML、JSON、TOML、HTML/CSS、Vue、Svelte、Terraform のような構造化シンボル抽出が弱い言語や、コメント・文字列・生テキストを探す場合は `search` を使う。
- テストを明示的に調べるのでなければ、まず `--exclude-tests` を付ける。
- 広い検索を始める前に `--path <text>` と繰り返し指定できる `--exclude-path <text>` を使って対象モジュールに絞る。
- 別のモデルやツールへ渡す前提で検索JSONをさらに細くしたいときは、`search` に `--snippet-lines <n>` を付ける。
- 候補パスの把握には `files` を使い、必要行だけ読むときは `excerpt` を使ってファイル全体を開かない。

## CLI（cdidxが利用可能な場合に推奨）

```bash
cdidx map --path src/ --exclude-tests --json
cdidx inspect "Authenticate" --exclude-tests
cdidx search "keyword" --path src/ --exclude-tests --snippet-lines 6
cdidx definition "ClassName" --path src/Services --body
cdidx callers "Authenticate" --lang csharp --exclude-tests
cdidx callees "HandleRequest" --path src/ --exclude-tests
cdidx symbols "ClassName" --lang csharp --exclude-tests
cdidx excerpt src/app.py --start 10 --end 20
cdidx files --lang python --path src/
cdidx status --json
```

## 直接SQLクエリ（cdidxが利用できない場合のフォールバック）

以下のクエリには `sqlite3` が必要です。未インストールの場合、ユーザーにインストールを提案してください:
- **macOS**: プリインストール済み
- **Linux**: `sudo apt install sqlite3`
- **Windows**: `winget install SQLite.SQLite` または `scoop install sqlite`

### 全文検索
```sql
SELECT f.path, c.start_line, c.content
FROM fts_chunks fc
JOIN chunks c ON c.id = fc.rowid
JOIN files f ON f.id = c.file_id
WHERE fts_chunks MATCH 'キーワード'
LIMIT 20;
```

### 関数・クラス名で検索
```sql
SELECT f.path, s.name, s.line
FROM symbols s
JOIN files f ON f.id = s.file_id
WHERE s.kind = 'function' AND s.name LIKE '%キーワード%';
```
````

### CI / フック向けインクリメンタル更新

プロジェクト全体を再インデックスする代わりに、変更のあったファイルだけを更新できます:

```bash
# 特定コミットの変更ファイルのみ更新（例: post-mergeフックで）
cdidx ./myproject --commits abc123 def456

# 特定ファイルのみ更新（例: エディタの保存フックで）
cdidx ./myproject --files src/app.cs src/utils.cs
```

これらのオプションにより、大規模コードベースでもリアルタイムにインデックスを最新に保つことが実用的になります。

### MCP サーバー（Claude Code、Cursor、Windsurf 等に対応）

cdidxには**MCP（Model Context Protocol）サーバー**が組み込まれています。MCPは、AIコーディングツールが外部プログラムと通信するための標準プロトコルです。`cdidx mcp` を実行すると、cdidxがstdin/stdoutで待機し、AIツールからの検索リクエストをJSONで受け取り、構築済みインデックスから即座に結果を返します。

ツール結果は `structuredContent` に構造化JSON、`content` に短い要約テキストを返すため、AIツールは巨大なテキストをパースせずに型付きデータを扱えます。

```
┌──────────────┐  stdin (JSON-RPC)  ┌──────────┐
│  Claude Code │ ──────────────────→ │  cdidx   │
│  / Cursor    │ ←────────────────── │  mcp     │
│  / Windsurf  │  stdout (JSON-RPC) │  server  │
└──────────────┘                    └──────────┘
```

**セットアップ — AIツールの設定ファイルに追加するだけ:**

Claude Code (`.claude/settings.json` または `.mcp.json`):

```json
{
  "mcpServers": {
    "cdidx": {
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

Cursor (`.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "cdidx": {
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

Windsurf (`.windsurf/mcp.json`):

```json
{
  "mcpServers": {
    "cdidx": {
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

GitHub Copilot (VS Code — `.vscode/mcp.json`):

```json
{
  "servers": {
    "cdidx": {
      "type": "stdio",
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

OpenAI Codex CLI (`codex.json` または `~/.codex/config.json`):

```json
{
  "mcpServers": {
    "cdidx": {
      "command": "cdidx",
      "args": ["mcp", "--db", ".cdidx/codeindex.db"]
    }
  }
}
```

設定するだけで、AIが以下のツールを直接呼び出せます:

| ツール | 説明 |
|---|---|
| `search` | コードチャンクの全文検索 |
| `definition` | シンボルの宣言と必要なら本体を再構成して取得 |
| `references` | 対応言語でインデックス済み参照を検索 |
| `callers` | 対応言語で指定シンボルの caller を列挙 |
| `callees` | 対応言語で指定シンボルの callee を列挙 |
| `symbols` | 関数・クラス・インターフェース・import・namespace を名前で検索 |
| `files` | インデックス済みファイル一覧 |
| `excerpt` | インデックス済みチャンクから特定行範囲を再構成 |
| `map` | 言語、モジュール、ホットスポット、推定エントリポイントを要約 |
| `analyze_symbol` | 定義、近傍シンボル、参照、caller、callee、ファイル情報をまとめて返す |
| `status` | データベース統計情報 |
| `index` | プロジェクトのインデックス作成・更新 |

CLAUDE.mdの設定やSQLテンプレートは不要 — AIがcdidxとネイティブに連携します。

### AIワークフローで grep/ripgrep より cdidx が優れる理由

| | `grep` / `rg` | `cdidx` |
|---|---|---|
| 出力形式 | プレーンテキスト（パース必要） | JSONライン（機械処理可能） |
| 大規模リポジトリでの検索速度 | 毎回全ファイルスキャン | 構築済みFTS5インデックス |
| シンボル認識 | なし | 関数、クラス、インポート |
| インクリメンタル更新 | N/A | `--commits`, `--files` |

## もっと詳しく

- [開発者ガイド](DEVELOPER_GUIDE.md) — アーキテクチャ、DBスキーマ、FTS5の内部構造、AI連携、設計判断
- [自己改善ループ](SELF_IMPROVEMENT.md) — AIが cdidx 自身を継続改善するときの、そのまま使える運用契約
