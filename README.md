# CodeIndex

> **[日本語版はこちら / Japanese version](#codeindex日本語)**

[![Build and Test](https://github.com/Widthdom/CodeIndex/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/dotnet.yml)
[![CodeQL](https://github.com/Widthdom/CodeIndex/actions/workflows/codeql.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/codeql.yml)
[![Release](https://github.com/Widthdom/CodeIndex/actions/workflows/release.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/release.yml)

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/License-MIT-blue)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white)

A CLI tool that indexes large codebases into a SQLite database, enabling AI to efficiently search and navigate code.

## Installation

```bash
# Build
dotnet build src/CodeIndex/CodeIndex.csproj -c Release

# Publish as a single binary
dotnet publish src/CodeIndex/CodeIndex.csproj -c Release -o ./publish

# Optional: add to PATH
# Linux / macOS
cp ./publish/CodeIndex /usr/local/bin/codeindex

# Windows (PowerShell — run as Administrator)
# Copy-Item .\publish\CodeIndex.exe C:\Tools\codeindex.exe
# Then add C:\Tools to your system PATH if not already there
```

## Usage

```bash
# Basic usage
codeindex <projectPath> [options]

# Options
#   --db <path>    Output database file path (default: codeindex.db)
#   --rebuild      Delete existing DB and rebuild from scratch
#   --verbose      Show verbose output

# Examples
codeindex /path/to/project
codeindex /path/to/project --db ./codeindex.db --rebuild
codeindex /path/to/project --verbose
```

## How it works

1. **Scan** — Recursively walks the project directory, filtering by known source file extensions and skipping common non-source directories (`node_modules`, `.git`, `build`, etc.)
2. **Index** — For each file, stores metadata (path, language, size, line count, checksum, modification time) and a snippet of the first 2000 characters
3. **Chunk** — Splits each file into 80-line chunks with 10-line overlap for granular full-text search
4. **Extract** — Uses regex-based extraction to identify symbols (functions, classes, imports) across multiple languages

Incremental mode (default) skips files that haven't changed since the last index.

## Why CodeIndex instead of grep?

On small projects, `grep` works fine. But as a codebase grows to tens of thousands of files, `grep` becomes a bottleneck — especially when an AI agent calls it repeatedly. CodeIndex solves this by **reading every file once at index time** and building a search structure so that queries never need to touch the original files again.

### The problem with grep

`grep -r "keyword" .` performs a brute-force linear scan: it opens every file, reads every line, and checks for a match. The tenth search costs the same as the first — every search repeats the full scan from scratch. The slowness is not about how fast individual files are read; it is about the fact that *every file must be read every time*, regardless of whether it contains the keyword.

### How CodeIndex avoids repeated scans

CodeIndex shifts the expensive work to a one-time indexing step. After that, searches are cheap lookups into a pre-built database.

**At index time** (runs once, then incrementally):

1. **Read** — Walk the project directory and read each source file.
2. **Chunk** — Split each file into 80-line blocks with 10-line overlap. The actual source text of each chunk is stored in the `chunks` table in SQLite. This means the database contains the code itself, not just file paths.
3. **Tokenize & build inverted index** — SQLite FTS5 processes the chunk text, breaks it into tokens (words), and builds an *inverted index*: a data structure that maps each token to the list of chunks that contain it (see [What is an inverted index?](#what-is-an-inverted-index) below).
4. **Extract symbols** — Identify function, class, and import names via regex and store them in the `symbols` table.

**At query time** (runs every search):

1. Look up the search term in the FTS5 inverted index → get matching chunk row IDs directly, without scanning any text.
2. Join to the `chunks` table to retrieve the 80-line code block and line numbers.
3. Join to the `files` table to get the file path and language (e.g., C#, Python, JavaScript).

No source files are opened. No directories are scanned. The entire search runs inside SQLite.

| Factor | `grep -r` | CodeIndex (SQLite FTS5) |
|---|---|---|
| **Search algorithm** | Linear scan of every file, every time | Token lookup in inverted index |
| **Repeated searches** | Same full cost each time | Near-instant after initial index |
| **Startup cost** | None | One-time indexing (incremental updates after) |
| **What is stored** | Nothing — reads files on the fly | Source text in chunks + inverted index of tokens |
| **Structured queries** | Text matching only | Filter by language, path, symbol kind, line range |
| **Symbol awareness** | None — just raw text | Knows function/class/import names and locations |
| **AI token cost** | Returns raw lines — noisy, high token usage | Returns precise chunks with file path and line numbers |

### Language-aware features

CodeIndex detects the programming language of each file from its extension (e.g., `.cs` → C#, `.py` → Python, `.ts` → TypeScript) and uses this information in two ways:

- **Filtering by language** — The `files.lang` column has a B-tree index, so queries like `WHERE lang = 'csharp'` efficiently narrow results to a single language. This is useful when you want to search only C# files in a mixed-language project, for example.
- **Language-specific symbol extraction** — The symbol extractor uses different regex patterns per language to accurately identify functions, classes, and imports. For instance, it understands that C# methods start with access modifiers (`public`, `private`), Python functions start with `def`, and Rust functions start with `fn`. This means symbol queries reflect each language's syntax, not a one-size-fits-all heuristic.

Currently supported languages for symbol extraction: Python, JavaScript/TypeScript, C#, Go, Rust, Java/Kotlin. Files in other languages are still fully indexed and searchable via FTS5 — only the symbol extraction step is skipped.

### What is SQLite FTS5?

[FTS5](https://www.sqlite.org/fts5.html) (Full-Text Search 5) is a SQLite extension that adds full-text search capabilities. It lets you write queries like `WHERE fts_chunks MATCH 'handleRequest'` and get results in milliseconds, even over millions of rows.

FTS5 works through a **virtual table** — a table that looks and behaves like a normal SQLite table (you can `SELECT` from it, join it, etc.) but stores its data in a specialized format optimized for text search. Under the hood, the virtual table maintains an inverted index rather than storing rows in the conventional B-tree format.

#### The `fts_chunks` table in detail

In CodeIndex, the FTS5 virtual table `fts_chunks` is created with this SQL:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks USING fts5(
    content,
    content='chunks',
    content_rowid='id'
);
```

Each part means:

| Parameter | Meaning |
|---|---|
| `USING fts5(...)` | Use the FTS5 engine to manage this virtual table |
| `content` | The column to index — corresponds to `chunks.content` (the actual code text) |
| `content='chunks'` | This is an **external-content table** — `fts_chunks` does not store a copy of the text. It references the text already stored in the `chunks` table. This avoids doubling storage. |
| `content_rowid='id'` | The `rowid` of each FTS5 entry matches `chunks.id`, so joining the two tables is a direct row lookup |

Because `fts_chunks` is an external-content table, CodeIndex must keep it in sync manually. When a file is indexed, the code inserts into both `chunks` and `fts_chunks` in the same transaction:

```csharp
// Insert the chunk text into the chunks table
INSERT INTO chunks (file_id, chunk_index, start_line, end_line, content) VALUES (...)

// Mirror the same text into fts_chunks so FTS5 can tokenize it
INSERT INTO fts_chunks (rowid, content) VALUES (last_insert_rowid(), @content)
```

When a file is re-indexed or deleted, the corresponding `fts_chunks` rows are deleted first, then the `chunks` rows:

```csharp
DELETE FROM fts_chunks WHERE rowid IN (SELECT id FROM chunks WHERE file_id = @fid)
DELETE FROM chunks WHERE file_id = @fid
```

This manual sync is the trade-off of external-content tables: you get smaller database files (no duplicated text), but you are responsible for keeping the FTS index consistent with the source table.

**What `fts_chunks` stores internally:** Only the inverted index — the mapping from each token to the list of `rowid`s that contain it. The actual code text lives in `chunks.content`. When a `MATCH` query runs, FTS5 returns matching `rowid`s, and the query joins back to `chunks` to retrieve the text.

### <a id="what-is-an-inverted-index"></a>What is an inverted index?

An inverted index is a data structure that maps each word (token) to the list of documents (or rows) that contain it — like the index at the back of a textbook.

For example, suppose three chunks contain the following code:

| Chunk ID | Content (simplified) |
|---|---|
| 1 | `handleRequest(ctx)` |
| 2 | `sendResponse(ctx)` |
| 3 | `handleRequest(req); sendResponse(res)` |

The inverted index built by FTS5 would look like:

| Token | Chunk IDs |
|---|---|
| `handleRequest` | 1, 3 |
| `sendResponse` | 2, 3 |
| `ctx` | 1, 2 |
| `req` | 3 |
| `res` | 3 |

When you search for `handleRequest`, FTS5 reads the entry for that token and immediately returns chunk IDs `{1, 3}` — no scanning required. This is how the database knows the likely matching locations in advance.

### B-tree indexes vs FTS5

CodeIndex uses two different kinds of indexes for different purposes.

#### What is a B-tree index?

A B-tree (balanced tree) is the default index structure in SQLite. It organizes values in a sorted, tree-shaped hierarchy — similar to how a phone book is sorted alphabetically.

For example, the index on `files.lang` might look conceptually like this:

```
            [go | python]
           /      |       \
    [csharp]  [java, kotlin]  [rust, typescript]
```

To find all C# files, SQLite walks down the tree: start at the root, go left (because `"csharp"` < `"go"`), and arrive at the leaf node — a few steps instead of scanning every row. This lookup takes O(log n) time.

B-tree indexes are created on columns like `files.path`, `files.lang`, `files.modified`, `chunks.file_id`, and `symbols.name`. They are good for:

- **Exact matches** — `WHERE lang = 'csharp'`
- **Range queries** — `WHERE modified > '2025-01-01'`
- **Sorting** — `ORDER BY path`

However, B-tree indexes cannot efficiently answer "which rows contain the word `handleRequest` somewhere in a text column?" — that would still require scanning every value. This is where FTS5 comes in.

#### How FTS5 differs

The FTS5 inverted index solves a different problem: full-text search inside code content. Instead of sorting values, it maps each token (word) to the rows that contain it (see [What is an inverted index?](#what-is-an-inverted-index) above). A `MATCH` query looks up the token directly — no tree traversal, no text scanning.

| | B-tree index | FTS5 inverted index |
|---|---|---|
| **Best for** | Exact match, range, sort | Full-text keyword search |
| **Lookup method** | Walk a sorted tree (O(log n)) | Look up token → row ID list |
| **Used on** | `path`, `lang`, `modified`, `file_id`, `name` | `chunks.content` (code text) |
| **Example query** | `WHERE lang = 'python'` | `WHERE fts_chunks MATCH 'handleRequest'` |

These two index types complement each other. A typical query might use FTS5 to find matching chunks and then use B-tree indexes to filter by language or file path.

### Database structure

CodeIndex builds a SQLite database with four main structures:

| Table | Purpose |
|---|---|
| **files** | One row per source file — path, language, size, line count, SHA256 checksum, modification time |
| **chunks** | Source text split into 80-line blocks (10-line overlap). Contains the actual code. |
| **symbols** | Functions, classes, and imports extracted by regex. Queryable by `kind` and `name`. |
| **fts_chunks** | FTS5 virtual table — inverted index over `chunks.content` for full-text `MATCH` queries |

### How the search works

When you run:
```sql
SELECT f.path, c.start_line, c.content
FROM fts_chunks fc
JOIN chunks c ON c.id = fc.rowid
JOIN files f ON f.id = c.file_id
WHERE fts_chunks MATCH 'handleRequest'
LIMIT 20;
```

1. FTS5 looks up `handleRequest` in its inverted index → gets a list of matching chunk `rowid`s directly
2. Joins back to `chunks` to get the 80-line code block with start/end line numbers
3. Joins to `files` to get the file path and language (e.g., C#, Python, JavaScript)

No files are opened. No directories are scanned. The entire search runs inside SQLite's optimized query engine.

### When to use which

| Scenario | Recommended tool |
|---|---|
| Quick one-off search in a small project | `grep` |
| Repeated searches across a large codebase | **CodeIndex** |
| AI agent performing multiple code lookups | **CodeIndex** |
| Finding all usages of a function by name | **CodeIndex** (`symbols` table) |
| Searching binary files or non-code content | `grep` |

## Git branch switching

The database does **not** store branch names. It always reflects the state of the working tree at the time of the last index run. This means:

- **You need to re-run CodeIndex after switching branches.** Simply run `codeindex <projectPath>` again. Because indexing is incremental, this is fast.
- **No branch name in queries.** You search the DB the same way regardless of which branch you are on. There is no need to add a branch filter to your `WHERE` clause.

What happens to each file during re-indexing after a branch switch:

| Situation | What CodeIndex does |
|---|---|
| File exists on both branches, **content unchanged** | Skipped (same `modified` timestamp) — instant |
| File exists on both branches, **content changed** | Re-indexed (old chunks/symbols deleted, new ones inserted) |
| File only on the **old branch** (deleted after checkout) | Purged from DB automatically |
| File only on the **new branch** (added after checkout) | Indexed as a new file |

In short, after `git checkout <branch> && codeindex .`, the database is fully consistent with the current branch. Files common to both branches that haven't changed incur almost no cost.

```mermaid
flowchart LR
    A[git checkout branch-B] --> B[codeindex .]
    B --> C{Per-file check}
    C -->|Unchanged| D[Skip]
    C -->|Changed| E[Re-index]
    C -->|Deleted| F[Purge from DB]
    C -->|New| G[Index as new]
    D & E & F & G --> H[DB = branch-B in sync]
```

## Prerequisites: sqlite3 CLI

AI agents (Claude Code, etc.) need the `sqlite3` command to query the generated database.

| OS | Status |
|---|---|
| **macOS** | Pre-installed. No action needed. |
| **Linux** | Usually pre-installed. If not: `sudo apt install sqlite3` (Debian/Ubuntu) or `sudo dnf install sqlite3` (Fedora). |
| **Windows** | Not included by default. Install with one of the methods below. |

### Installing sqlite3 on Windows

**Option A: winget (recommended)**

Open PowerShell and run:
```powershell
winget install SQLite.SQLite
```

**Option B: scoop**

[Scoop](https://scoop.sh/) is a command-line package manager for Windows. If you don't have it yet, open PowerShell and install it first:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression
```

Then install sqlite3:
```powershell
scoop install sqlite
```

**Option C: Manual download**
1. Go to https://www.sqlite.org/download.html
2. Download **sqlite-tools-win-x64-XXXXXXX.zip** (or win32 for 32-bit)
3. Extract to a folder (e.g. `C:\sqlite`)
4. Add that folder to your system PATH:
   ```powershell
   # Run as Administrator
   [Environment]::SetEnvironmentVariable("Path", $env:Path + ";C:\sqlite", "Machine")
   ```
5. Open a new terminal and verify: `sqlite3 --version`

## AI Integration

To let AI use the generated `codeindex.db`, place a `CLAUDE.md` file in your project root with the following content:

````markdown
# Code Search Rules

This project has a `codeindex.db` file.
When searching code, you **must** query this SQLite database.
Do not use `find`, `grep`, or `ls -R` to scan files directly.

## Prerequisites: sqlite3

To query the database, the `sqlite3` CLI must be available.

- **macOS**: Pre-installed. No action needed.
- **Linux**: Usually pre-installed. If not: `sudo apt install sqlite3` (Debian/Ubuntu) or `sudo dnf install sqlite3` (Fedora).
- **Windows**: Run `winget install SQLite.SQLite` in PowerShell, or `scoop install sqlite` if you use Scoop.

## Basic Queries

### Search by path
```sql
SELECT path, lang, lines, modified
FROM files
WHERE path LIKE '%keyword%'
ORDER BY modified DESC LIMIT 20;
```

### Full-text search in code
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

---

<a id="codeindex日本語"></a>
# CodeIndex（日本語）

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/License-MIT-blue)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white)

大規模コードベースをSQLiteデータベースにインデックスするCLIツールです。AIがコードを効率的に検索・ナビゲートできるDBファイルを生成します。

## インストール

```bash
# ビルド
dotnet build src/CodeIndex/CodeIndex.csproj -c Release

# 単一バイナリとしてパブリッシュ
dotnet publish src/CodeIndex/CodeIndex.csproj -c Release -o ./publish

# 任意: PATHに追加
# Linux / macOS
cp ./publish/CodeIndex /usr/local/bin/codeindex

# Windows（PowerShell — 管理者として実行）
# Copy-Item .\publish\CodeIndex.exe C:\Tools\codeindex.exe
# C:\Tools がPATHに含まれていない場合は追加してください
```

## 使い方

```bash
# 基本的な使い方
codeindex <プロジェクトパス> [オプション]

# オプション
#   --db <パス>    出力するDBファイルのパス（デフォルト: codeindex.db）
#   --rebuild      既存DBを削除して再構築
#   --verbose      詳細ログを表示

# 例
codeindex /path/to/project
codeindex /path/to/project --db ./codeindex.db --rebuild
codeindex /path/to/project --verbose
```

## 動作の仕組み

1. **走査** — プロジェクトディレクトリを再帰的に走査し、既知のソースファイル拡張子でフィルタリング。`node_modules`、`.git`、`build`などの非ソースディレクトリはスキップ
2. **インデックス** — 各ファイルのメタデータ（パス、言語、サイズ、行数、チェックサム、更新日時）と先頭2000文字のスニペットを保存
3. **チャンク分割** — 各ファイルを80行ごとに10行の重複を持たせて分割し、きめ細かい全文検索を実現
4. **シンボル抽出** — 正規表現による簡易的なシンボル抽出（関数、クラス、インポート）を複数言語で実施

インクリメンタルモード（デフォルト）では、前回のインデックス以降に変更のないファイルをスキップします。

## なぜgrepではなくCodeIndexなのか？

小規模プロジェクトなら `grep` で十分です。しかしファイルが数万規模になると `grep` はボトルネックになります。特にAIエージェントが繰り返し検索を実行するケースで顕著です。CodeIndexは**すべてのファイルを一度だけ読み込んで検索用の構造を構築する**ことで、以降の検索で元のファイルを一切開かずに済むようにします。

### grepの問題点

`grep -r "keyword" .` は力任せの線形スキャンです。毎回すべてのファイルを開き、すべての行を読み、マッチを確認します。10回目の検索でも1回目と同じコストがかかります。遅さの原因は個々のファイルの読み込み速度ではなく、*キーワードを含むかどうかに関係なく、毎回すべてのファイルを読まなければならない*という点にあります。

### CodeIndexが繰り返しスキャンを回避する仕組み

CodeIndexは重い処理を一度きりのインデックス作成ステップに集約します。それ以降の検索は、事前に構築されたデータベースへの軽い参照で済みます。

**インデックス作成時**（初回のみ実行、以降はインクリメンタル更新）：

1. **読み込み** — プロジェクトディレクトリを走査し、各ソースファイルを読む。
2. **チャンク分割** — 各ファイルを80行ブロック（10行重複）に分割。各チャンクのソースコード本文はSQLiteの`chunks`テーブルに格納される。つまり、データベースにはファイルパスだけでなくコード本文そのものが保存される。
3. **トークン化と転置インデックスの構築** — SQLite FTS5がチャンクのテキストを処理し、トークン（単語）に分割し、*転置インデックス*を構築する。転置インデックスとは、各トークンからそれを含むチャンクのリストへのマッピングのこと（後述の[転置インデックスとは？](#転置インデックスとは)を参照）。
4. **シンボル抽出** — 正規表現で関数・クラス・インポートの名前を識別し、`symbols`テーブルに格納。

**検索時**（毎回の検索で実行）：

1. 検索キーワードをFTS5の転置インデックスで参照 → テキストをスキャンせずにマッチするチャンクの行IDを直接取得。
2. `chunks`テーブルにJOINして80行のコードブロックと行番号を取得。
3. `files`テーブルにJOINしてファイルパスと言語（例: C#、Python、JavaScriptなど）を取得。

ソースファイルは一切開かれず、ディレクトリのスキャンも不要です。検索全体がSQLite内で完結します。

| 観点 | `grep -r` | CodeIndex (SQLite FTS5) |
|---|---|---|
| **検索アルゴリズム** | 毎回すべてのファイルを線形スキャン | 転置インデックスによるトークン参照 |
| **繰り返し検索** | 毎回同じフルコスト | 初回インデックス後はほぼ即時 |
| **初期コスト** | なし | 一度だけのインデックス作成（以降はインクリメンタル更新） |
| **保存される情報** | なし — 毎回ファイルを直接読む | チャンク化されたソースコード + トークンの転置インデックス |
| **構造化クエリ** | テキストマッチのみ | 言語・パス・シンボル種別・行範囲でフィルタ可能 |
| **シンボル認識** | なし — 生テキストのみ | 関数・クラス・インポートの名前と位置を認識 |
| **AIトークンコスト** | 生の行を返す — ノイズが多くトークン消費大 | ファイルパスと行番号付きの的確なチャンクを返す |

### 言語ごとの最適化

CodeIndexは各ファイルの拡張子からプログラミング言語を検出し（例: `.cs` → C#、`.py` → Python、`.ts` → TypeScript）、以下の2つの用途で活用します：

- **言語によるフィルタリング** — `files.lang`列にはB-treeインデックスがあるため、`WHERE lang = 'csharp'` のようなクエリで効率的に特定言語のファイルだけに絞り込めます。複数言語が混在するプロジェクトでC#ファイルだけを検索したい場合などに便利です。
- **言語別のシンボル抽出** — シンボル抽出器は言語ごとに異なる正規表現パターンを使い、関数・クラス・インポートを的確に識別します。例えば、C#のメソッドはアクセス修飾子（`public`、`private`）で始まること、Pythonの関数は`def`で始まること、Rustの関数は`fn`で始まることを理解しています。これにより、シンボル検索は画一的なヒューリスティックではなく、各言語の構文に即した結果を返します。

現在シンボル抽出に対応している言語: Python、JavaScript/TypeScript、C#、Go、Rust、Java/Kotlin。その他の言語のファイルもFTS5による全文検索・チャンク検索は問題なく利用できます。スキップされるのはシンボル抽出のステップのみです。

### SQLite FTS5とは？

[FTS5](https://www.sqlite.org/fts5.html)（Full-Text Search 5）は、SQLiteに全文検索機能を追加する拡張です。`WHERE fts_chunks MATCH 'handleRequest'` のようなクエリを書くだけで、数百万行のデータからでもミリ秒単位で結果を得ることができます。

FTS5は**仮想テーブル**を通じて動作します。仮想テーブルとは、通常のSQLiteテーブルと同じように見え、同じように操作できる（`SELECT`やJOINが可能）テーブルですが、内部ではテキスト検索に最適化された特殊な形式でデータを保持します。通常のB-tree形式で行を格納するのではなく、転置インデックスを管理します。

#### `fts_chunks`テーブルの詳細

CodeIndexでは、FTS5仮想テーブル `fts_chunks` は以下のSQLで作成されます：

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks USING fts5(
    content,
    content='chunks',
    content_rowid='id'
);
```

各パラメータの意味は以下の通りです：

| パラメータ | 意味 |
|---|---|
| `USING fts5(...)` | FTS5エンジンでこの仮想テーブルを管理する |
| `content` | インデックス対象の列 — `chunks.content`（コード本文）に対応 |
| `content='chunks'` | **外部コンテンツテーブル**であることの宣言。`fts_chunks`はテキストのコピーを持たず、`chunks`テーブルにすでに格納されているテキストを参照する。これによりストレージの二重化を回避する。 |
| `content_rowid='id'` | FTS5エントリの`rowid`が`chunks.id`と一致する。これにより2テーブルのJOINが直接的な行参照で済む |

`fts_chunks`は外部コンテンツテーブルであるため、CodeIndexは手動で同期を保つ必要があります。ファイルのインデックス時には、同一トランザクション内で`chunks`と`fts_chunks`の両方に挿入します：

```csharp
// チャンクのテキストをchunksテーブルに挿入
INSERT INTO chunks (file_id, chunk_index, start_line, end_line, content) VALUES (...)

// 同じテキストをfts_chunksにも挿入し、FTS5にトークン化させる
INSERT INTO fts_chunks (rowid, content) VALUES (last_insert_rowid(), @content)
```

ファイルの再インデックスや削除時には、まず`fts_chunks`の該当行を削除してから`chunks`の行を削除します：

```csharp
DELETE FROM fts_chunks WHERE rowid IN (SELECT id FROM chunks WHERE file_id = @fid)
DELETE FROM chunks WHERE file_id = @fid
```

この手動同期は外部コンテンツテーブルのトレードオフです。テキストを二重保持しないためDBファイルが小さくなる一方、FTSインデックスとソーステーブルの整合性を自分で維持する必要があります。

**`fts_chunks`が内部に保持するもの：** 転置インデックスのみ — 各トークンからそれを含む`rowid`のリストへのマッピング。コード本文は`chunks.content`に格納されています。`MATCH`クエリが実行されると、FTS5はマッチする`rowid`を返し、クエリが`chunks`にJOINしてテキストを取得します。

### <a id="転置インデックスとは"></a>転置インデックスとは？

転置インデックスとは、各単語（トークン）からそれを含む文書（行）のリストへのマッピングです。教科書の巻末にある索引と同じ仕組みです。

例えば、3つのチャンクに以下のコードが含まれているとします：

| チャンクID | 内容（簡略化） |
|---|---|
| 1 | `handleRequest(ctx)` |
| 2 | `sendResponse(ctx)` |
| 3 | `handleRequest(req); sendResponse(res)` |

FTS5が構築する転置インデックスは以下のようになります：

| トークン | チャンクID |
|---|---|
| `handleRequest` | 1, 3 |
| `sendResponse` | 2, 3 |
| `ctx` | 1, 2 |
| `req` | 3 |
| `res` | 3 |

`handleRequest`を検索すると、FTS5はそのトークンのエントリを読んでチャンクID `{1, 3}` を即座に返します。テキストのスキャンは不要です。これが、データベースがマッチしそうな場所を事前に把握できる仕組みです。

### B-treeインデックスとFTS5の違い

CodeIndexは目的に応じて2種類のインデックスを使い分けています。

#### B-treeインデックスとは？

B-tree（平衡木）はSQLiteのデフォルトのインデックス構造です。値をソートされたツリー状の階層に整理します。電話帳がアルファベット順にソートされているのと同じ考え方です。

例えば、`files.lang`に対するインデックスは概念的に以下のような形になります：

```
            [go | python]
           /      |       \
    [csharp]  [java, kotlin]  [rust, typescript]
```

C#のファイルを探す場合、SQLiteはツリーを辿ります。ルートから開始し、左に進み（`"csharp"` < `"go"` のため）、リーフノードに到達します。全行をスキャンする代わりに数ステップで完了します。この参照はO(log n)の計算量です。

B-treeインデックスは`files.path`、`files.lang`、`files.modified`、`chunks.file_id`、`symbols.name`などの列に作成されており、以下の操作に適しています：

- **完全一致** — `WHERE lang = 'csharp'`
- **範囲検索** — `WHERE modified > '2025-01-01'`
- **ソート** — `ORDER BY path`

ただし、B-treeインデックスは「テキスト列のどこかに `handleRequest` という単語を含む行はどれか？」という問いには効率的に答えられません。その場合は全行のスキャンが必要になります。ここでFTS5が登場します。

#### FTS5との違い

FTS5の転置インデックスは、コード内容の全文検索という別の問題を解決します。値をソートするのではなく、各トークン（単語）からそれを含む行へのマッピングを持ちます（前述の[転置インデックスとは？](#転置インデックスとは)を参照）。`MATCH`クエリはトークンを直接参照するため、ツリーの走査もテキストのスキャンも不要です。

| | B-treeインデックス | FTS5転置インデックス |
|---|---|---|
| **得意な操作** | 完全一致、範囲検索、ソート | 全文キーワード検索 |
| **参照方法** | ソート済みツリーを辿る（O(log n)） | トークン → 行IDリストを参照 |
| **適用対象** | `path`, `lang`, `modified`, `file_id`, `name` | `chunks.content`（コード本文） |
| **クエリ例** | `WHERE lang = 'python'` | `WHERE fts_chunks MATCH 'handleRequest'` |

この2種類のインデックスは補完的に機能します。典型的なクエリでは、FTS5でマッチするチャンクを見つけた後、B-treeインデックスで言語やファイルパスによる絞り込みを行います。

### データベース構造

CodeIndexは4つの主要構造を持つSQLiteデータベースを構築します：

| テーブル | 用途 |
|---|---|
| **files** | ソースファイル1件につき1行 — パス、言語、サイズ、行数、SHA256チェックサム、更新日時 |
| **chunks** | 80行ブロック（10行重複）に分割されたソースコード本文を格納 |
| **symbols** | 正規表現で抽出した関数・クラス・インポート。`kind`と`name`で検索可能 |
| **fts_chunks** | FTS5仮想テーブル — `chunks.content`に対する転置インデックスで`MATCH`クエリによる全文検索を実現 |

### 検索の仕組み

以下のクエリを実行すると：
```sql
SELECT f.path, c.start_line, c.content
FROM fts_chunks fc
JOIN chunks c ON c.id = fc.rowid
JOIN files f ON f.id = c.file_id
WHERE fts_chunks MATCH 'handleRequest'
LIMIT 20;
```

1. FTS5が転置インデックスから `handleRequest` を参照 → マッチするチャンクの`rowid`リストを直接取得
2. `chunks`にJOINして80行のコードブロックと開始・終了行番号を取得
3. `files`にJOINしてファイルパスと言語（例: C#、Python、JavaScriptなど）を取得

ファイルは一切開かれません。ディレクトリのスキャンも不要です。検索全体がSQLiteの最適化されたクエリエンジン内で完結します。

### 使い分けの目安

| シナリオ | 推奨ツール |
|---|---|
| 小規模プロジェクトでの単発検索 | `grep` |
| 大規模コードベースでの繰り返し検索 | **CodeIndex** |
| AIエージェントによる複数回のコード探索 | **CodeIndex** |
| 関数名による全使用箇所の特定 | **CodeIndex**（`symbols`テーブル） |
| バイナリファイルや非コードの検索 | `grep` |

## Gitブランチ切り替え時の挙動

データベースにブランチ名は**保存されません**。DBは常に、最後にインデックスを実行した時点のワーキングツリーの状態を反映します。

- **ブランチ切り替え後はCodeIndexの再実行が必要です。** `codeindex <projectPath>` を再度実行してください。インクリメンタル処理のため高速です。
- **検索時にブランチ名の指定は不要です。** どのブランチにいても同じクエリで検索できます。`WHERE`句にブランチ名を含める必要はありません。

ブランチ切り替え後の再インデックスで、各ファイルに起こること:

| 状況 | CodeIndexの動作 |
|---|---|
| 両方のブランチに存在し、**内容が変わっていない** | スキップ（`modified`タイムスタンプが同一）— ほぼ即時 |
| 両方のブランチに存在し、**内容が変わっている** | 再インデックス（旧チャンク・シンボル削除後に新規挿入） |
| **旧ブランチにのみ**存在（checkout後に消えたファイル） | DBから自動パージ |
| **新ブランチにのみ**存在（checkout後に追加されたファイル） | 新規ファイルとしてインデックス |

つまり `git checkout <branch> && codeindex .` を実行すれば、データベースは現在のブランチと完全に整合します。両方のブランチに共通で変更のないファイルはほぼコストがかかりません。

```mermaid
flowchart LR
    A[git checkout branch-B] --> B[codeindex .]
    B --> C{ファイルごとに判定}
    C -->|未変更| D[スキップ]
    C -->|変更あり| E[再インデックス]
    C -->|消えた| F[DBからパージ]
    C -->|新規| G[新規インデックス]
    D & E & F & G --> H[DB = branch-Bと完全一致]
```

## 前提条件: sqlite3 CLI

AIエージェント（Claude Code等）が生成されたデータベースを検索するには `sqlite3` コマンドが必要です。

| OS | 状況 |
|---|---|
| **macOS** | プリインストール済み。追加作業不要。 |
| **Linux** | 通常プリインストール済み。未導入の場合: `sudo apt install sqlite3`（Debian/Ubuntu）または `sudo dnf install sqlite3`（Fedora）。 |
| **Windows** | デフォルトでは未同梱。以下のいずれかの方法でインストール。 |

### Windowsでのsqlite3インストール方法

**方法A: winget（推奨）**

PowerShellを開いて以下を実行してください:
```powershell
winget install SQLite.SQLite
```

**方法B: scoop**

[Scoop](https://scoop.sh/)はWindows向けのコマンドラインパッケージマネージャです。未導入の場合は、まずPowerShellを開いてScoopをインストールしてください:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression
```

その後、sqlite3をインストール:
```powershell
scoop install sqlite
```

**方法C: 手動ダウンロード**
1. https://www.sqlite.org/download.html にアクセス
2. **sqlite-tools-win-x64-XXXXXXX.zip**（32bitの場合はwin32版）をダウンロード
3. 任意のフォルダに展開（例: `C:\sqlite`）
4. そのフォルダをシステムPATHに追加:
   ```powershell
   # 管理者として実行
   [Environment]::SetEnvironmentVariable("Path", $env:Path + ";C:\sqlite", "Machine")
   ```
5. 新しいターミナルを開いて確認: `sqlite3 --version`

## AIとの連携

CodeIndexが生成した `codeindex.db` をAIに活用させるには、プロジェクトルートに `CLAUDE.md` を置き、以下の内容を記述してください。

````markdown
# コードベース検索ルール

このプロジェクトには `codeindex.db` があります。
コードを検索する際は **必ず** このDBをSQLiteで検索してください。
`find`, `grep`, `ls -R` などのファイル直接スキャンは禁止します。

## 前提条件: sqlite3

データベースを検索するには `sqlite3` CLIが必要です。

- **macOS**: プリインストール済み。追加作業不要。
- **Linux**: 通常プリインストール済み。未導入の場合: `sudo apt install sqlite3`（Debian/Ubuntu）または `sudo dnf install sqlite3`（Fedora）。
- **Windows**: PowerShellで `winget install SQLite.SQLite` を実行。またはScoopを使う場合は `scoop install sqlite`。

## 基本的な検索クエリ

### パスで探す
```sql
SELECT path, lang, lines, modified
FROM files
WHERE path LIKE '%キーワード%'
ORDER BY modified DESC LIMIT 20;
```

### コード内容を全文検索
```sql
SELECT f.path, c.start_line, c.content
FROM fts_chunks fc
JOIN chunks c ON c.id = fc.rowid
JOIN files f ON f.id = c.file_id
WHERE fts_chunks MATCH 'キーワード'
LIMIT 20;
```

### 関数・クラス名で探す
```sql
SELECT f.path, s.name, s.line
FROM symbols s
JOIN files f ON f.id = s.file_id
WHERE s.kind = 'function' AND s.name LIKE '%キーワード%';
```
````
