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
3. Join to the `files` table to get the file path and language.

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

### What is SQLite FTS5?

[FTS5](https://www.sqlite.org/fts5.html) (Full-Text Search 5) is a SQLite extension that adds full-text search capabilities. It lets you write queries like `WHERE fts_chunks MATCH 'handleRequest'` and get results in milliseconds, even over millions of rows.

FTS5 works through a **virtual table** — a table that looks and behaves like a normal SQLite table (you can `SELECT` from it, join it, etc.) but stores its data in a specialized format optimized for text search. Under the hood, the virtual table maintains an inverted index rather than storing rows in the conventional B-tree format.

In CodeIndex, the FTS5 virtual table `fts_chunks` is defined as an **external-content table**: it does not duplicate the chunk text, but references the text already stored in the `chunks` table. It only stores the inverted index itself.

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

CodeIndex uses two different kinds of indexes for different purposes:

- **B-tree indexes** (standard SQLite indexes) are created on columns like `files.path`, `files.lang`, `files.modified`, `chunks.file_id`, and `symbols.name`. These are good for exact matches and range queries (e.g., "find all Python files" or "find files modified after date X"). They work like a sorted lookup table.
- **FTS5 inverted index** is used for full-text search inside code content. It answers the question "which chunks contain this word?" without scanning every chunk. This is what makes keyword search fast.

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
3. Joins to `files` to get the file path and language

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
3. `files`テーブルにJOINしてファイルパスと言語を取得。

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

### SQLite FTS5とは？

[FTS5](https://www.sqlite.org/fts5.html)（Full-Text Search 5）は、SQLiteに全文検索機能を追加する拡張です。`WHERE fts_chunks MATCH 'handleRequest'` のようなクエリを書くだけで、数百万行のデータからでもミリ秒単位で結果を得ることができます。

FTS5は**仮想テーブル**を通じて動作します。仮想テーブルとは、通常のSQLiteテーブルと同じように見え、同じように操作できる（`SELECT`やJOINが可能）テーブルですが、内部ではテキスト検索に最適化された特殊な形式でデータを保持します。通常のB-tree形式で行を格納するのではなく、転置インデックスを管理します。

CodeIndexでは、FTS5仮想テーブル `fts_chunks` は**外部コンテンツテーブル**として定義されており、チャンクのテキストを複製するのではなく、すでに`chunks`テーブルに格納されているテキストを参照します。`fts_chunks`自体が保持するのは転置インデックスのみです。

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

CodeIndexは目的に応じて2種類のインデックスを使い分けています：

- **B-treeインデックス**（SQLiteの標準インデックス）は、`files.path`、`files.lang`、`files.modified`、`chunks.file_id`、`symbols.name`などの列に作成されます。完全一致や範囲検索に適しています（例：「Pythonファイルをすべて取得」「特定日時以降に更新されたファイルを検索」）。ソート済みの参照テーブルのように動作します。
- **FTS5転置インデックス**は、コード内容の全文検索に使われます。「このキーワードを含むチャンクはどれか？」という問いに、全チャンクをスキャンせずに回答します。キーワード検索が高速な理由はここにあります。

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
3. `files`にJOINしてファイルパスと言語を取得

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
