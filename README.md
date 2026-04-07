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

A CLI tool that indexes large codebases into a SQLite database for fast search. Works for both humans and AI agents.

## Installation

### 1. Build

```bash
dotnet build src/CodeIndex/CodeIndex.csproj -c Release
dotnet publish src/CodeIndex/CodeIndex.csproj -c Release -o ./publish
```

### 2. Add to PATH

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

### 3. Verify

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

### Search code

```bash
cdidx search "authenticate"              # full-text search
cdidx search "handleRequest" --lang go   # filter by language
cdidx search "TODO" --limit 50           # more results
```

### Search symbols (functions, classes, etc.)

```bash
cdidx symbols UserService              # find by name
cdidx symbols --kind class             # all classes
cdidx symbols --kind function --lang python
```

### List files

```bash
cdidx files                            # all indexed files
cdidx files --lang csharp              # only C# files
```

### Check status

```bash
cdidx status
```

## Options

| Option | Applies to | Description |
|---|---|---|
| `--db <path>` | All commands | Database file path (default: `codeindex.db`) |
| `--json` | All commands | JSON output (default for search/symbols/files) |
| `--no-json` | Query commands | Force human-readable output |
| `--limit <n>` | Query commands | Max results (default: 20) |
| `--lang <lang>` | Query commands | Filter by language |
| `--kind <kind>` | `symbols` | Filter by symbol kind (function/class/import) |
| `--rebuild` | `index` | Delete existing DB and rebuild |
| `--verbose` | `index` | Show detailed per-file output |
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

cdidx scans your project directory, splits each source file into overlapping chunks, and stores everything in a SQLite database with FTS5 full-text search. Incremental mode (default) skips files that haven't changed, so re-indexing after a branch switch is fast.

## Git branch switching

The database reflects the working tree at the time of the last index. After switching branches, simply re-run `cdidx .` — incremental mode makes this fast.

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
| C++ | `.cpp` | yes |
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

cdidx is designed as an AI-first code search tool. All query commands output JSON lines by default, making them easy to parse programmatically.

### Setup: Add to CLAUDE.md

To let AI agents use the generated `codeindex.db`, place a `CLAUDE.md` in your project root:

````markdown
# Code Search Rules

This project has a `codeindex.db` file.
When searching code, **query this SQLite database** instead of using `find`, `grep`, or `ls -R`.

## Keeping the index up to date

After editing files, update the database so search results stay accurate:

```bash
cdidx . --files path/to/changed_file.cs   # update specific files you modified
cdidx . --commits HEAD                     # update all files changed in the last commit
cdidx .                                    # full incremental update (skips unchanged files)
```

**Rule: whenever you modify source files, run one of the above before your next search.**

## CLI (recommended)

```bash
cdidx search "keyword"           # full-text search (JSON lines)
cdidx symbols "ClassName"        # structured symbol search
cdidx files --lang python        # list indexed files
cdidx status --json              # DB stats
```

## Direct SQL queries

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

### Why cdidx over grep/ripgrep for AI?

| | `grep` / `rg` | `cdidx` |
|---|---|---|
| Output format | Plain text (needs parsing) | JSON lines (machine-ready) |
| Search speed on large repos | Scans every file each time | Pre-built FTS5 index |
| Symbol awareness | None | Functions, classes, imports |
| Incremental update | N/A | `--commits`, `--files` |

## More

- [Developer Guide](DEVELOPER_GUIDE.md) — Architecture, database schema, FTS5 internals, AI integration, design decisions

---

<a id="codeindex日本語"></a>
# CodeIndex（日本語）

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/License-MIT-blue)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white)

大規模コードベースをSQLiteデータベースにインデックスし、高速検索を実現するCLIツールです。人間にもAIエージェントにも対応しています。

## インストール

### 1. ビルド

```bash
dotnet build src/CodeIndex/CodeIndex.csproj -c Release
dotnet publish src/CodeIndex/CodeIndex.csproj -c Release -o ./publish
```

### 2. PATHに追加

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

### 3. 確認

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

### コード検索

```bash
cdidx search "authenticate"              # 全文検索
cdidx search "handleRequest" --lang go   # 言語でフィルタ
cdidx search "TODO" --limit 50           # 結果数を増やす
```

### シンボル検索（関数、クラスなど）

```bash
cdidx symbols UserService              # 名前で検索
cdidx symbols --kind class             # すべてのクラス
cdidx symbols --kind function --lang python
```

### ファイル一覧

```bash
cdidx files                            # 全インデックス済みファイル
cdidx files --lang csharp              # C#ファイルのみ
```

### 状態確認

```bash
cdidx status
```

## オプション一覧

| オプション | 対象 | 説明 |
|---|---|---|
| `--db <path>` | 全コマンド | DBファイルパス（デフォルト: `codeindex.db`） |
| `--json` | 全コマンド | JSON出力（search/symbols/filesはデフォルト） |
| `--no-json` | クエリ系 | 人間向け出力を強制 |
| `--limit <n>` | クエリ系 | 最大結果数（デフォルト: 20） |
| `--lang <lang>` | クエリ系 | 言語でフィルタ |
| `--kind <kind>` | `symbols` | シンボル種別でフィルタ（function/class/import） |
| `--rebuild` | `index` | 既存DBを削除して再構築 |
| `--verbose` | `index` | ファイルごとの詳細出力 |
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

cdidxはプロジェクトディレクトリを走査し、各ソースファイルを重複を持つチャンクに分割し、FTS5全文検索付きのSQLiteデータベースに格納します。インクリメンタルモード（デフォルト）では変更のないファイルをスキップするため、ブランチ切り替え後の再インデックスも高速です。

## Gitブランチ切り替え

データベースはインデックス実行時のワーキングツリーを反映します。ブランチ切り替え後は `cdidx .` を再実行してください。インクリメンタルモードなので高速です。

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
| C++ | `.cpp` | yes |
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

cdidxはAIファーストのコード検索ツールとして設計されています。すべてのクエリコマンドはデフォルトでJSONライン出力を行い、プログラムからのパースが容易です。

### セットアップ: CLAUDE.mdに追加

AIエージェントに `codeindex.db` を活用させるには、プロジェクトルートに `CLAUDE.md` を配置してください:

````markdown
# コードベース検索ルール

このプロジェクトには `codeindex.db` があります。
コードを検索する際は `find`, `grep`, `ls -R` ではなく**このSQLiteデータベースを検索**してください。

## インデックスの最新化

ファイルを編集したら、検索結果を正確に保つためにデータベースを更新してください:

```bash
cdidx . --files path/to/changed_file.cs   # 変更したファイルだけ更新
cdidx . --commits HEAD                     # 直前のコミットで変更されたファイルを更新
cdidx .                                    # フルインクリメンタル更新（未変更ファイルはスキップ）
```

**ルール: ソースファイルを修正したら、次の検索の前に上記のいずれかを実行すること。**

## CLI（推奨）

```bash
cdidx search "keyword"           # 全文検索（JSONライン出力）
cdidx symbols "ClassName"        # 構造化シンボル検索
cdidx files --lang python        # インデックス済みファイル一覧
cdidx status --json              # DB統計情報
```

## 直接SQLクエリ

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

### AIにとって grep/ripgrep より cdidx が優れる理由

| | `grep` / `rg` | `cdidx` |
|---|---|---|
| 出力形式 | プレーンテキスト（パース必要） | JSONライン（機械処理可能） |
| 大規模リポジトリでの検索速度 | 毎回全ファイルスキャン | 構築済みFTS5インデックス |
| シンボル認識 | なし | 関数、クラス、インポート |
| インクリメンタル更新 | N/A | `--commits`, `--files` |

## もっと詳しく

- [開発者ガイド](DEVELOPER_GUIDE.md) — アーキテクチャ、DBスキーマ、FTS5の内部構造、AI連携、設計判断
