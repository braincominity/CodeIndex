# cdidx (CodeIndex) — Development Guide for AI

## Project overview

cdidx is a .NET 8 CLI tool that indexes source code into a SQLite database (FTS5) for AI-powered search. It supports both human-readable and machine-readable (JSON) output, making it usable by both humans and AI agents. Assembly name is `cdidx` (like `rg` for ripgrep).

## Build & test

```bash
dotnet build
dotnet test
dotnet run --project src/CodeIndex -- <command> [options]
```

## CLI commands

```bash
# Indexing
cdidx index <projectPath> [--db <path>] [--rebuild] [--verbose] [--json]
cdidx <projectPath>                          # shorthand for 'index'

# Query (default output: human-readable; use --json for AI consumption)
cdidx search <query> [--db <path>] [--limit <n>] [--lang <lang>] [--json]
cdidx symbols [query] [--kind <kind>] [--lang <lang>] [--limit <n>]
cdidx files [query] [--lang <lang>] [--limit <n>]
cdidx status [--json]
```

## Architecture

```
src/CodeIndex/
  Program.cs               — CLI entry point, subcommand routing, --json support
  Cli/ConsoleUi.cs         — Spinner, progress bar, banner, easter egg, version, usage text
  Cli/GitHelper.cs         — Git diff-tree helper for --commits option
  Database/DbContext.cs     — SQLite connection, schema init (WAL, FTS5, triggers, busy_timeout)
  Database/DbWriter.cs      — UPSERT (ON CONFLICT DO UPDATE), batch insert, stale file purge
  Database/DbReader.cs      — Query operations (FTS search, symbol lookup, file listing, status)
  Indexer/FileIndexer.cs    — Directory scan, language detection, FileRecord building
  Indexer/ChunkSplitter.cs  — 80-line chunks with 10-line overlap
  Indexer/SymbolExtractor.cs — Regex-based symbol extraction (multi-language)
  Models/                   — FileRecord, ChunkRecord, SymbolRecord (plain DTOs)
tests/CodeIndex.Tests/
  UnitTest1.cs              — xUnit tests (chunker, symbols, indexer, DB integration, DbReader queries)
```

## Key design decisions

- **No ORM** — Raw `Microsoft.Data.Sqlite` with parameterized queries. Keep it simple.
- **Incremental by default** — Compares `modified` timestamp and SHA256 checksum; skips unchanged files.
- **Stale file purge** — Before indexing, removes DB entries for files no longer on disk (branch switch support).
- **Batch commits** — 500 records per transaction for write performance. Supports nesting via SAVEPOINT.
- **FTS5** — `fts_chunks` virtual table mirrors `chunks.content` for full-text search. Sync via database triggers (AFTER INSERT/DELETE/UPDATE on chunks). FTS5 optimize runs after indexing.
- **Regex symbol extraction** — Intentionally simple. Accuracy is secondary to speed and portability.
- **Human-readable default** — All commands default to human-readable output. Use `--json` for machine-readable JSON lines (AI-friendly).
- **Structured exit codes** — 0=success, 1=usage error, 2=not found, 3=database error.

## Conventions

- Comments are bilingual (English / Japanese), e.g. `// Enable WAL mode / WALモードを有効化`
- Documentation (README, CHANGELOG) is structured: English first, then Japanese.
- No unnecessary packages — `System.CommandLine` was removed in favor of manual arg parsing.

---

# cdidx (CodeIndex) — AI向け開発ガイド

## プロジェクト概要

cdidxは、ソースコードをSQLiteデータベース（FTS5）にインデックスする.NET 8 CLIツール。人間向けとAIエージェント向け（JSON）の両方の出力に対応。アセンブリ名は`cdidx`（ripgrepの`rg`のように短縮）。

## ビルド・テスト

```bash
dotnet build
dotnet test
dotnet run --project src/CodeIndex -- <command> [options]
```

## CLIコマンド

```bash
# インデックス作成
cdidx index <projectPath> [--db <path>] [--rebuild] [--verbose] [--json]
cdidx <projectPath>                          # 'index'の省略形

# クエリ（デフォルト出力: 人間向け; --jsonでAI向け出力）
cdidx search <query> [--db <path>] [--limit <n>] [--lang <lang>] [--json]
cdidx symbols [query] [--kind <kind>] [--lang <lang>] [--limit <n>]
cdidx files [query] [--lang <lang>] [--limit <n>]
cdidx status [--json]
```

## アーキテクチャ

```
src/CodeIndex/
  Program.cs               — CLIエントリポイント、サブコマンドルーティング、--jsonサポート
  Cli/ConsoleUi.cs         — スピナー、プログレスバー、バナー、イースターエッグ、バージョン、使い方
  Cli/GitHelper.cs         — --commitsオプション用のgit diff-treeヘルパー
  Database/DbContext.cs     — SQLite接続、スキーマ初期化（WAL, FTS5, トリガー, busy_timeout）
  Database/DbWriter.cs      — UPSERT（ON CONFLICT DO UPDATE）、バッチ挿入、古いファイルのパージ
  Database/DbReader.cs      — クエリ操作（FTS検索、シンボル検索、ファイル一覧、ステータス）
  Indexer/FileIndexer.cs    — ディレクトリ走査、言語検出、FileRecord構築
  Indexer/ChunkSplitter.cs  — 80行チャンク（10行重複）
  Indexer/SymbolExtractor.cs — 正規表現によるシンボル抽出（多言語対応）
  Models/                   — FileRecord, ChunkRecord, SymbolRecord（プレーンDTO）
tests/CodeIndex.Tests/
  UnitTest1.cs              — xUnitテスト（チャンク、シンボル、インデクサー、DB統合、DbReaderクエリ）
```

## 主要な設計判断

- **ORMなし** — `Microsoft.Data.Sqlite`でパラメータ化クエリを直接使用。シンプルに保つ。
- **デフォルトでインクリメンタル** — `modified`タイムスタンプとSHA256チェックサムを比較し、未変更ファイルをスキップ。
- **古いファイルのパージ** — インデックス前にディスク上に存在しないファイルをDBから削除（ブランチ切り替え対応）。
- **バッチコミット** — 書き込み性能のため1トランザクション500レコード。SAVEPOINTによるネスト対応。
- **FTS5** — `fts_chunks`仮想テーブルが`chunks.content`をミラーして全文検索を提供。データベーストリガー（chunksのAFTER INSERT/DELETE/UPDATE）で同期。インデックス後にFTS5 optimizeを実行。
- **正規表現シンボル抽出** — 意図的にシンプル。速度とポータビリティを精度より優先。
- **人間向けがデフォルト** — 全コマンドのデフォルト出力は人間向け。`--json`でAI向けJSONライン出力に切り替え。
- **構造化終了コード** — 0=成功、1=引数エラー、2=未検出、3=DBエラー。

## コーディング規約

- コメントは英日併記（例: `// Enable WAL mode / WALモードを有効化`）
- ドキュメント（README, CHANGELOG）は前半英語、後半日本語の構成。
- 不要なパッケージは入れない — `System.CommandLine`は手動引数解析に置き換えて削除済み。
