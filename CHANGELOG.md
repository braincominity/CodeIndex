# Changelog

All notable changes to this project will be documented in this file.

The English section comes first, followed by a Japanese translation.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## English

### [1.0.1] - 2026-04-07

#### Fixed

- **FTS5 orphan entries on re-indexing** — `INSERT OR REPLACE` on the `files` table triggered `ON DELETE CASCADE` on `chunks`, but the FTS5 virtual table (`fts_chunks`) was not covered by CASCADE. Added `CleanExistingFileData()` to explicitly remove FTS entries before re-upserting. Affected: `Database/DbWriter.cs`, `Program.cs`. Tests: `CleanExistingFileData_PreventsFtsOrphans`.

- **FTS5 MATCH query injection** — User input containing FTS5 operators (`*`, `"`, `AND`, `OR`, `NOT`, `NEAR`) was passed directly to `MATCH`, causing syntax errors or unexpected results. Each token is now quoted as a literal phrase. Affected: `Database/DbReader.cs`.

- **LIKE wildcard injection** — `%` and `_` in user queries for `SearchSymbols` and `ListFiles` were interpreted as SQL LIKE wildcards. Added `ESCAPE` clause and input escaping. Affected: `Database/DbReader.cs`.

- **Connection string injection** — Database path containing `;` could inject additional SQLite connection parameters. Now uses `SqliteConnectionStringBuilder`. Affected: `Database/DbContext.cs`.

- **Git argument injection** — Commit IDs passed to `git diff-tree` were not validated. Added regex whitelist and `--` option terminator. Affected: `Cli/GitHelper.cs`.

- **CancellationTokenSource leak** — Spinner CTS was `Cancel()`'d but never `Dispose()`'d. Affected: `Cli/ConsoleUi.cs`.

#### Changed

- **Checksum-based incremental detection** — `GetUnchangedFileId()` now falls back to SHA256 checksum comparison when timestamps differ (e.g. after `git checkout`), avoiding unnecessary re-indexing. Affected: `Database/DbWriter.cs`. Tests: `GetUnchangedFileId_MatchesByChecksumWhenTimestampDiffers`.

- **Added `idx_symbols_file` index** — New index on `symbols(file_id)` for faster deletes and `ListFiles` subquery. Affected: `Database/DbContext.cs`.

- **Optimized CRLF normalization** — `ChunkSplitter.Split()` now skips `Replace()` when no `\r` is present, avoiding unnecessary allocation. Affected: `Indexer/ChunkSplitter.cs`.

- **Extracted CLI helpers** — Moved spinner, progress bar, banner, easter egg, version loading, and usage text to `Cli/ConsoleUi.cs`. Moved git operations to `Cli/GitHelper.cs`. `Program.cs` reduced from 959 to 686 lines. Affected: `Program.cs`, `Cli/ConsoleUi.cs` (new), `Cli/GitHelper.cs` (new).

---

### [1.0.0] - 2026-04-06

#### Added

- **Core indexing engine** — Scans project directories recursively, detecting 28 file extensions across 24 languages (Python, JavaScript, TypeScript, C#, Go, Rust, Java, Kotlin, Swift, Ruby, PHP, C/C++, SQL, HTML, CSS, SCSS, Vue, Svelte, Terraform, Shell, Markdown, YAML, JSON, TOML). Skips common non-source directories (`.git`, `node_modules`, `__pycache__`, `venv`, `dist`, `build`, `.next`, `.idea`, `vendor`, etc.) and lock files (`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`). Affected: `Indexer/FileIndexer.cs`.

- **SQLite database with FTS5 full-text search** — Three core tables (`files`, `chunks`, `symbols`) with indexes on path, language, modified time, file_id, and symbol name. FTS5 virtual table (`fts_chunks`) enables fast full-text search across all code chunks. WAL mode enabled for better concurrent performance. Affected: `Database/DbContext.cs`, `Database/DbWriter.cs`.

- **Chunked content storage** — Files are split into 80-line chunks with 10-line overlap between consecutive chunks, enabling granular full-text search with sufficient context at chunk boundaries. Affected: `Indexer/ChunkSplitter.cs`.

- **Regex-based symbol extraction** — Extracts function, class, and import symbols from Python (`def`, `async def`, `class`), JavaScript/TypeScript (`function`, `class`, `import`, `export`), C# (`public/private/protected` + `class`/methods), Go (`func`), Rust (`fn`, `struct`, `impl`), and Java/Kotlin (`class`, `fun`, `void`, `public`). Affected: `Indexer/SymbolExtractor.cs`.

- **Incremental indexing** — Compares file modification timestamps against the database; unchanged files are skipped entirely. Reduces re-indexing time for large codebases with few changes. Affected: `Database/DbWriter.cs`, `Program.cs`.

- **Stale file purging for branch switching** — Automatically detects and removes database entries for files that no longer exist on disk (e.g., after `git checkout` to a different branch). Runs before indexing in incremental mode. Affected: `Database/DbWriter.cs`, `Program.cs`.

- **Batch commit optimization** — Database writes are committed in batches of 500 records per transaction, balancing memory usage and write performance. Affected: `Database/DbWriter.cs`.

- **CLI interface** — Accepts project path as positional argument with `--db` (output path), `--rebuild` (full re-index), `--verbose` (detailed per-file logging), and `--help` options. Displays progress every 50 files and a summary with file/chunk/symbol counts and elapsed time. Affected: `Program.cs`.

- **CLAUDE.md AI search prompt template** — Bilingual (English/Japanese) reference document with ready-to-use SQL queries for path search, full-text search, symbol lookup, language filtering, and file overview. Includes notes on branch switching and database staleness detection. Affected: `CLAUDE.md`.

- **Test suite** — 58 xUnit tests covering ChunkSplitter (6 tests: small files, exact chunk size, large files with overlap, empty files, sequential indices, CRLF normalization), SymbolExtractor (18 tests: Python, JavaScript, C#, Go, Rust, Java, Kotlin, Ruby, PHP, Swift, C, C++, TypeScript interface/enum, null/unknown languages, line numbering), FileIndexer (8 tests: language detection for known/unknown extensions, directory/file skipping, record building, case-insensitive directory skipping, CRLF normalization in records, oversized file rejection), Database integration (12 tests: schema creation, upsert, conflict handling, incremental skip, FTS population, symbol insertion, stale file purging, drop-all, file deletion by path, deletion of non-existent path, deletion isolation), and DbReader queries (14 tests: full-text search, empty results, language filtering, result limit, symbol search by name, symbol filtering by kind/language, combined filters, file listing, file filtering by language/name, symbol count in file results, status counts, language breakdown). Affected: `tests/CodeIndex.Tests/UnitTest1.cs`.

---

## 日本語

### [1.0.1] - 2026-04-07

#### 修正

- **再インデックス時のFTS5孤立エントリ** — `files`テーブルへの`INSERT OR REPLACE`が`chunks`の`ON DELETE CASCADE`を発火するが、FTS5仮想テーブル（`fts_chunks`）はCASCADE対象外だった。`CleanExistingFileData()`を追加し、再UPSERT前にFTSエントリを明示的に削除。対象: `Database/DbWriter.cs`, `Program.cs`。テスト: `CleanExistingFileData_PreventsFtsOrphans`。

- **FTS5 MATCHクエリインジェクション** — FTS5演算子（`*`, `"`, `AND`, `OR`, `NOT`, `NEAR`）を含むユーザー入力が直接`MATCH`に渡され、構文エラーや予期しない結果が発生していた。各トークンをリテラルフレーズとして引用するよう修正。対象: `Database/DbReader.cs`。

- **LIKEワイルドカードインジェクション** — `SearchSymbols`と`ListFiles`のクエリで`%`と`_`がSQL LIKEワイルドカードとして解釈されていた。`ESCAPE`句と入力エスケープを追加。対象: `Database/DbReader.cs`。

- **接続文字列インジェクション** — `;`を含むデータベースパスで追加のSQLite接続パラメータが注入可能だった。`SqliteConnectionStringBuilder`を使用するよう修正。対象: `Database/DbContext.cs`。

- **Git引数インジェクション** — `git diff-tree`に渡されるコミットIDが未検証だった。正規表現ホワイトリストと`--`オプション終端を追加。対象: `Cli/GitHelper.cs`。

- **CancellationTokenSourceリーク** — スピナーのCTSが`Cancel()`されるだけで`Dispose()`されていなかった。対象: `Cli/ConsoleUi.cs`。

#### 変更

- **チェックサムによるインクリメンタル検出** — `GetUnchangedFileId()`がタイムスタンプ不一致時にSHA256チェックサム比較にフォールバックし（例: `git checkout`後）、不要な再インデックスを回避。対象: `Database/DbWriter.cs`。テスト: `GetUnchangedFileId_MatchesByChecksumWhenTimestampDiffers`。

- **`idx_symbols_file`インデックス追加** — `symbols(file_id)`への新インデックスで削除と`ListFiles`サブクエリを高速化。対象: `Database/DbContext.cs`。

- **CRLF正規化の最適化** — `ChunkSplitter.Split()`で`\r`が含まれない場合は`Replace()`をスキップし、不要なアロケーションを回避。対象: `Indexer/ChunkSplitter.cs`。

- **CLIヘルパーの分離** — スピナー、プログレスバー、バナー、イースターエッグ、バージョン読み込み、使い方テキストを`Cli/ConsoleUi.cs`に、Git操作を`Cli/GitHelper.cs`に分離。`Program.cs`は959行から686行に削減。対象: `Program.cs`, `Cli/ConsoleUi.cs`（新規）, `Cli/GitHelper.cs`（新規）。

---

### [1.0.0] - 2026-04-06

#### 追加

- **コアインデックスエンジン** — プロジェクトディレクトリを再帰的に走査し、24言語にわたる28種類のファイル拡張子を検出（Python, JavaScript, TypeScript, C#, Go, Rust, Java, Kotlin, Swift, Ruby, PHP, C/C++, SQL, HTML, CSS, SCSS, Vue, Svelte, Terraform, Shell, Markdown, YAML, JSON, TOML）。一般的な非ソースディレクトリ（`.git`, `node_modules`, `__pycache__`, `venv`, `dist`, `build`, `.next`, `.idea`, `vendor`等）とロックファイル（`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`）をスキップ。対象: `Indexer/FileIndexer.cs`。

- **FTS5全文検索対応SQLiteデータベース** — 3つのコアテーブル（`files`, `chunks`, `symbols`）にパス、言語、更新日時、file_id、シンボル名のインデックスを設定。FTS5仮想テーブル（`fts_chunks`）により全コードチャンクの高速全文検索が可能。WALモード有効化で並行性能を向上。対象: `Database/DbContext.cs`, `Database/DbWriter.cs`。

- **チャンク分割コンテンツ保存** — ファイルを80行ごとに分割し、連続するチャンク間に10行の重複を持たせることで、チャンク境界での十分なコンテキストを保ちつつきめ細かい全文検索を実現。対象: `Indexer/ChunkSplitter.cs`。

- **正規表現によるシンボル抽出** — Python（`def`, `async def`, `class`）、JavaScript/TypeScript（`function`, `class`, `import`, `export`）、C#（`public/private/protected` + `class`/メソッド）、Go（`func`）、Rust（`fn`, `struct`, `impl`）、Java/Kotlin（`class`, `fun`, `void`, `public`）から関数、クラス、インポートシンボルを抽出。対象: `Indexer/SymbolExtractor.cs`。

- **インクリメンタルインデックス** — ファイルの更新日時をデータベースと比較し、未変更ファイルを完全にスキップ。変更の少ない大規模コードベースの再インデックス時間を削減。対象: `Database/DbWriter.cs`, `Program.cs`。

- **ブランチ切り替え対応の古いファイルパージ** — ディスク上に存在しなくなったファイル（例：`git checkout`で別ブランチに切り替え後）のデータベースエントリを自動検出・削除。インクリメンタルモードではインデックス処理前に実行。対象: `Database/DbWriter.cs`, `Program.cs`。

- **バッチコミット最適化** — データベースへの書き込みを1トランザクションあたり500レコードのバッチでコミットし、メモリ使用量と書き込み性能のバランスを最適化。対象: `Database/DbWriter.cs`。

- **CLIインターフェース** — プロジェクトパスを位置引数として受け取り、`--db`（出力パス）、`--rebuild`（完全再インデックス）、`--verbose`（ファイルごとの詳細ログ）、`--help`オプションに対応。50ファイルごとに進捗を表示し、ファイル数・チャンク数・シンボル数と経過時間のサマリーを出力。対象: `Program.cs`。

- **CLAUDE.md AI検索プロンプトテンプレート** — 英語・日本語併記のリファレンスドキュメント。パス検索、全文検索、シンボル検索、言語フィルタリング、ファイル概要の即使用可能なSQLクエリを収録。ブランチ切り替えとデータベースの鮮度検出に関する注記を含む。対象: `CLAUDE.md`。

- **テストスイート** — 58件のxUnitテスト。ChunkSplitter（6件: 小ファイル、ちょうどチャンクサイズ、重複ありの大ファイル、空ファイル、連番インデックス、CRLF正規化）、SymbolExtractor（18件: Python, JavaScript, C#, Go, Rust, Java, Kotlin, Ruby, PHP, Swift, C, C++, TypeScript interface/enum, null/未知言語, 行番号）、FileIndexer（8件: 既知/未知拡張子の言語検出、ディレクトリ/ファイルスキップ、レコード構築、大文字小文字非区別のディレクトリスキップ、CRLF正規化、サイズ超過ファイル拒否）、Database統合（12件: スキーマ作成、upsert、競合処理、インクリメンタルスキップ、FTS反映、シンボル挿入、古いファイルパージ、全削除、パスによるファイル削除、存在しないパスの削除、削除の分離性）、DbReaderクエリ（14件: 全文検索、空結果、言語フィルタ、結果数制限、名前によるシンボル検索、種別・言語によるシンボルフィルタ、複合フィルタ、ファイル一覧、言語・名前によるファイルフィルタ、ファイル結果のシンボル数、ステータスカウント、言語内訳）をカバー。対象: `tests/CodeIndex.Tests/UnitTest1.cs`。
