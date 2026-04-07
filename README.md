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

A CLI tool that indexes large codebases into a SQLite database for fast search. Works for both humans and AI agents.

## Installation

```bash
# Build
dotnet build src/CodeIndex/CodeIndex.csproj -c Release

# Publish as a single binary
dotnet publish src/CodeIndex/CodeIndex.csproj -c Release -o ./publish

# Optional: add to PATH
# Linux / macOS
cp ./publish/cdidx /usr/local/bin/cdidx

# Windows (PowerShell — run as Administrator)
# Copy-Item .\publish\cdidx.exe C:\Tools\cdidx.exe
# Then add C:\Tools to your system PATH if not already there
```

## Quick Start

### Index a project

```bash
cdidx ./myproject
cdidx ./myproject --rebuild     # full rebuild from scratch
cdidx ./myproject --verbose     # show per-file details
cdidx ./myproject --json        # output summary as JSON
```

### Search code (full-text)

```bash
cdidx search "authenticate"              # FTS5 full-text search
cdidx search "handleRequest" --lang go   # filter by language
cdidx search "TODO" --limit 50           # more results
```

Output (JSON lines by default — one result per line):
```json
{"path":"src/auth.py","lang":"python","start_line":1,"end_line":80,"content":"def authenticate(user):\n ...","score":-1.5}
```

### Search symbols

```bash
cdidx symbols UserService              # find by name
cdidx symbols --kind class             # all classes
cdidx symbols --kind function --lang python  # Python functions only
```

### List files

```bash
cdidx files                            # all indexed files
cdidx files --lang csharp              # only C# files
cdidx files api                        # files matching "api" in path
```

### Database status

```bash
cdidx status                           # human-readable summary
cdidx status --json                    # JSON output
```

### Options

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

1. **Scan** — Recursively walks the project directory, skipping non-source directories (`node_modules`, `.git`, `build`, etc.)
2. **Index** — Stores file metadata (path, language, size, line count, checksum) and a snippet of the first 2000 characters
3. **Chunk** — Splits each file into 80-line chunks with 10-line overlap for full-text search
4. **Extract** — Identifies symbols (functions, classes, imports) via regex across 13 languages

Incremental mode (default) skips files that haven't changed since the last index.

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
| Shell | `.sh` | — |
| SQL | `.sql` | — |
| Markdown | `.md` | — |
| YAML | `.yaml`, `.yml` | — |
| JSON | `.json` | — |
| TOML | `.toml` | — |
| HTML | `.html` | — |
| CSS | `.css`, `.scss` | — |
| Vue | `.vue` | — |
| Svelte | `.svelte` | — |
| Terraform | `.tf` | — |

All languages are fully searchable via FTS5. Languages with **Symbols** also support structured queries by function/class/import name.

## Git branch switching

The database reflects the working tree at the time of the last index. After switching branches, simply re-run `cdidx .` — incremental mode makes this fast.

| Situation | What happens |
|---|---|
| File unchanged across branches | Skipped (instant) |
| File content changed | Re-indexed |
| File deleted after checkout | Purged from DB |
| File added after checkout | Indexed as new |

## Prerequisites: sqlite3

AI agents that query the database directly via SQL need the `sqlite3` CLI.

| OS | Status |
|---|---|
| **macOS** | Pre-installed |
| **Linux** | Usually pre-installed. If not: `sudo apt install sqlite3` |
| **Windows** | `winget install SQLite.SQLite` or `scoop install sqlite` |

## AI Integration

To let AI agents use the generated `codeindex.db`, place a `CLAUDE.md` in your project root:

````markdown
# Code Search Rules

This project has a `codeindex.db` file.
When searching code, **query this SQLite database** instead of using `find`, `grep`, or `ls -R`.

## Queries

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

Alternatively, if `cdidx` is on PATH, AI agents can use the CLI directly:

```bash
cdidx search "keyword"           # JSON lines output, ready for parsing
cdidx symbols "ClassName"        # structured symbol search
```

## More

- [Developer Guide](DEVELOPER_GUIDE.md) — Architecture, database internals, FTS5 details, B-tree vs inverted index

---

<a id="cdidx日本語"></a>
# cdidx（日本語）

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/License-MIT-blue)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white)

大規模コードベースをSQLiteデータベースにインデックスし、高速検索を実現するCLIツールです。人間にもAIエージェントにも対応しています。

## インストール

```bash
# ビルド
dotnet build src/CodeIndex/CodeIndex.csproj -c Release

# 単一バイナリとしてパブリッシュ
dotnet publish src/CodeIndex/CodeIndex.csproj -c Release -o ./publish

# 任意: PATHに追加
# Linux / macOS
cp ./publish/cdidx /usr/local/bin/cdidx

# Windows（PowerShell — 管理者として実行）
# Copy-Item .\publish\cdidx.exe C:\Tools\cdidx.exe
# C:\Tools がPATHに含まれていない場合は追加してください
```

## クイックスタート

### プロジェクトをインデックス

```bash
cdidx ./myproject
cdidx ./myproject --rebuild     # 完全再構築
cdidx ./myproject --verbose     # ファイルごとの詳細表示
cdidx ./myproject --json        # サマリーをJSON出力
```

### コード検索（全文検索）

```bash
cdidx search "authenticate"              # FTS5全文検索
cdidx search "handleRequest" --lang go   # 言語でフィルタ
cdidx search "TODO" --limit 50           # 結果数を増やす
```

出力（デフォルトはJSONライン — 1行1結果）:
```json
{"path":"src/auth.py","lang":"python","start_line":1,"end_line":80,"content":"def authenticate(user):\n ...","score":-1.5}
```

### シンボル検索

```bash
cdidx symbols UserService              # 名前で検索
cdidx symbols --kind class             # すべてのクラス
cdidx symbols --kind function --lang python  # Pythonの関数のみ
```

### ファイル一覧

```bash
cdidx files                            # 全インデックス済みファイル
cdidx files --lang csharp              # C#ファイルのみ
cdidx files api                        # パスに"api"を含むファイル
```

### データベース状態

```bash
cdidx status                           # 人間向けサマリー
cdidx status --json                    # JSON出力
```

### オプション一覧

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

1. **走査** — プロジェクトディレクトリを再帰的に走査し、非ソースディレクトリ（`node_modules`、`.git`、`build`等）をスキップ
2. **インデックス** — ファイルメタデータ（パス、言語、サイズ、行数、チェックサム）と先頭2000文字のスニペットを保存
3. **チャンク分割** — 各ファイルを80行ごとに10行の重複を持たせて分割し、全文検索を実現
4. **シンボル抽出** — 正規表現により13言語でシンボル（関数、クラス、インポート）を識別

インクリメンタルモード（デフォルト）では、前回から変更のないファイルをスキップします。

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
| Shell | `.sh` | — |
| SQL | `.sql` | — |
| Markdown | `.md` | — |
| YAML | `.yaml`, `.yml` | — |
| JSON | `.json` | — |
| TOML | `.toml` | — |
| HTML | `.html` | — |
| CSS | `.css`, `.scss` | — |
| Vue | `.vue` | — |
| Svelte | `.svelte` | — |
| Terraform | `.tf` | — |

全言語がFTS5による全文検索に対応。**シンボル**が「yes」の言語は関数・クラス・インポート名での構造化検索にも対応しています。

## Gitブランチ切り替え

データベースはインデックス実行時のワーキングツリーを反映します。ブランチ切り替え後は `cdidx .` を再実行してください。インクリメンタルモードなので高速です。

| 状況 | 動作 |
|---|---|
| ブランチ間でファイル未変更 | スキップ（即時） |
| ファイル内容が変更 | 再インデックス |
| checkout後にファイル削除 | DBからパージ |
| checkout後にファイル追加 | 新規インデックス |

## 前提条件: sqlite3

AIエージェントがDBを直接SQL検索する場合、`sqlite3` CLIが必要です。

| OS | 状況 |
|---|---|
| **macOS** | プリインストール済み |
| **Linux** | 通常プリインストール済み。未導入時: `sudo apt install sqlite3` |
| **Windows** | `winget install SQLite.SQLite` または `scoop install sqlite` |

## AIとの連携

AIエージェントに `codeindex.db` を活用させるには、プロジェクトルートに `CLAUDE.md` を配置してください:

````markdown
# コードベース検索ルール

このプロジェクトには `codeindex.db` があります。
コードを検索する際は `find`, `grep`, `ls -R` ではなく**このSQLiteデータベースを検索**してください。

## クエリ

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

`cdidx` がPATH上にある場合、AIエージェントはCLIを直接使うこともできます:

```bash
cdidx search "keyword"           # JSONライン出力、パース可能
cdidx symbols "ClassName"        # 構造化シンボル検索
```

## もっと詳しく

- [開発者ガイド](DEVELOPER_GUIDE.md) — アーキテクチャ、DB内部構造、FTS5の詳細、B-treeと転置インデックスの比較
