# Changelog

All notable changes to this project will be documented in this file.

The English section comes first, followed by a Japanese translation.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## English

### [1.0.0] - 2026-04-06

#### Added

- **Core indexing engine** — Scans project directories recursively, detecting 28 file extensions across 24 languages (Python, JavaScript, TypeScript, C#, Go, Rust, Java, Kotlin, Swift, Ruby, PHP, C/C++, SQL, HTML, CSS, SCSS, Vue, Svelte, Terraform, Shell, Markdown, YAML, JSON, TOML). Skips common non-source directories (`.git`, `node_modules`, `__pycache__`, `venv`, `dist`, `build`, `.next`, `.idea`, `vendor`, etc.) and lock files (`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`). Affected: `Indexer/FileIndexer.cs`.

- **SQLite database with FTS5 full-text search** — Three core tables (`files`, `chunks`, `symbols`) with indexes on path, language, modified time, file_id, and symbol name. FTS5 virtual table (`fts_chunks`) enables fast full-text search across all code chunks. WAL mode enabled for better concurrent performance. Affected: `Database/DbContext.cs`, `Database/DbWriter.cs`.

- **Chunked content storage** — Files are split into 80-line chunks with 10-line overlap between consecutive chunks, enabling granular full-text search with sufficient context at chunk boundaries. Affected: `Indexer/ChunkSplitter.cs`.

- **Regex-based symbol extraction** — Extracts function, class, and import symbols from Python (`def`, `async def`, `class`), JavaScript/TypeScript (`function`, `class`, `import`, `export`), C# (`public/private/protected` + `class`/methods), Go (`func`), Rust (`fn`, `struct`, `impl`), and Java/Kotlin (`class`, `fun`, `void`, `public`). Affected: `Indexer/SymbolExtractor.cs`.

- **Incremental indexing** — Compares file modification timestamps against the database; unchanged files are skipped entirely. Reduces re-indexing time for large codebases with few changes. Affected: `Database/DbWriter.cs`, `Program.cs`.

- **Stale file purging for branch switching** — Automatically detects and removes database entries for files that no longer exist on disk (e.g., after `git checkout` to a different branch). Runs before indexing in incremental mode. Affected: `Database/DbWriter.cs`, `Program.cs`.

- **Batch commit optimization** — Database writes are committed in batches of 500 records per transaction, balancing memory usage and write performance. Affected: `Database/DbWriter.cs`.

- **CLI interface** — Accepts project path as positional argument with `--db` (output path), `--rebuild` (full re-index), `--verbose` (detailed per-file logging), and `--help` options. Displays progress every 500 files and a summary with file/chunk/symbol counts and elapsed time. Affected: `Program.cs`.

- **CLAUDE.md AI search prompt template** — Bilingual (English/Japanese) reference document with ready-to-use SQL queries for path search, full-text search, symbol lookup, language filtering, and file overview. Includes notes on branch switching and database staleness detection. Affected: `CLAUDE.md`.

- **Test suite** — 41 xUnit tests covering ChunkSplitter (5 tests: small files, exact chunk size, large files with overlap, empty files, sequential indices), SymbolExtractor (9 tests: Python, JavaScript, C#, Go, Rust, null/unknown languages, line numbering), FileIndexer (5 tests: language detection for known/unknown extensions, directory/file skipping, record building), and Database integration (8 tests: schema creation, upsert, conflict handling, incremental skip, FTS population, symbol insertion, stale file purging, drop-all). Affected: `tests/CodeIndex.Tests/UnitTest1.cs`.

---

## 日本語

### [1.0.0] - 2026-04-06

#### 追加

- **コアインデックスエンジン** — プロジェクトディレクトリを再帰的に走査し、24言語にわたる28種類のファイル拡張子を検出（Python, JavaScript, TypeScript, C#, Go, Rust, Java, Kotlin, Swift, Ruby, PHP, C/C++, SQL, HTML, CSS, SCSS, Vue, Svelte, Terraform, Shell, Markdown, YAML, JSON, TOML）。一般的な非ソースディレクトリ（`.git`, `node_modules`, `__pycache__`, `venv`, `dist`, `build`, `.next`, `.idea`, `vendor`等）とロックファイル（`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`）をスキップ。対象: `Indexer/FileIndexer.cs`。

- **FTS5全文検索対応SQLiteデータベース** — 3つのコアテーブル（`files`, `chunks`, `symbols`）にパス、言語、更新日時、file_id、シンボル名のインデックスを設定。FTS5仮想テーブル（`fts_chunks`）により全コードチャンクの高速全文検索が可能。WALモード有効化で並行性能を向上。対象: `Database/DbContext.cs`, `Database/DbWriter.cs`。

- **チャンク分割コンテンツ保存** — ファイルを80行ごとに分割し、連続するチャンク間に10行の重複を持たせることで、チャンク境界での十分なコンテキストを保ちつつきめ細かい全文検索を実現。対象: `Indexer/ChunkSplitter.cs`。

- **正規表現によるシンボル抽出** — Python（`def`, `async def`, `class`）、JavaScript/TypeScript（`function`, `class`, `import`, `export`）、C#（`public/private/protected` + `class`/メソッド）、Go（`func`）、Rust（`fn`, `struct`, `impl`）、Java/Kotlin（`class`, `fun`, `void`, `public`）から関数、クラス、インポートシンボルを抽出。対象: `Indexer/SymbolExtractor.cs`。

- **インクリメンタルインデックス** — ファイルの更新日時をデータベースと比較し、未変更ファイルを完全にスキップ。変更の少ない大規模コードベースの再インデックス時間を削減。対象: `Database/DbWriter.cs`, `Program.cs`。

- **ブランチ切り替え対応の古いファイルパージ** — ディスク上に存在しなくなったファイル（例：`git checkout`で別ブランチに切り替え後）のデータベースエントリを自動検出・削除。インクリメンタルモードではインデックス処理前に実行。対象: `Database/DbWriter.cs`, `Program.cs`。

- **バッチコミット最適化** — データベースへの書き込みを1トランザクションあたり500レコードのバッチでコミットし、メモリ使用量と書き込み性能のバランスを最適化。対象: `Database/DbWriter.cs`。

- **CLIインターフェース** — プロジェクトパスを位置引数として受け取り、`--db`（出力パス）、`--rebuild`（完全再インデックス）、`--verbose`（ファイルごとの詳細ログ）、`--help`オプションに対応。500ファイルごとに進捗を表示し、ファイル数・チャンク数・シンボル数と経過時間のサマリーを出力。対象: `Program.cs`。

- **CLAUDE.md AI検索プロンプトテンプレート** — 英語・日本語併記のリファレンスドキュメント。パス検索、全文検索、シンボル検索、言語フィルタリング、ファイル概要の即使用可能なSQLクエリを収録。ブランチ切り替えとデータベースの鮮度検出に関する注記を含む。対象: `CLAUDE.md`。

- **テストスイート** — 41件のxUnitテスト。ChunkSplitter（5件: 小ファイル、ちょうどチャンクサイズ、重複ありの大ファイル、空ファイル、連番インデックス）、SymbolExtractor（9件: Python, JavaScript, C#, Go, Rust, null/未知言語, 行番号）、FileIndexer（5件: 既知/未知拡張子の言語検出、ディレクトリ/ファイルスキップ、レコード構築）、Database統合（8件: スキーマ作成、upsert、競合処理、インクリメンタルスキップ、FTS反映、シンボル挿入、古いファイルパージ、全削除）をカバー。対象: `tests/CodeIndex.Tests/UnitTest1.cs`。
