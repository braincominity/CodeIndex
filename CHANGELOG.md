# Changelog

All notable changes to this project will be documented in this file.

The English section comes first, followed by a Japanese translation.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## English

### [Unreleased]

#### Added

- **Core indexing engine** — Scans project directories recursively, detecting 33 file extensions across 24 languages (Python, JavaScript, TypeScript, C#, Go, Rust, Java, Kotlin, Swift, Ruby, PHP, C/C++, SQL, HTML, CSS, SCSS, Vue, Svelte, Terraform, Shell, Markdown, YAML, JSON, TOML). Skips common non-source directories (`.git`, `node_modules`, `__pycache__`, `venv`, `dist`, `build`, `.next`, `.idea`, `vendor`, etc.) and lock files (`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`). Affected: `Indexer/FileIndexer.cs`.

- **SQLite database with FTS5 full-text search** — Three core tables (`files`, `chunks`, `symbols`) with indexes on language, modified time, file_id, and symbol name. FTS5 virtual table (`fts_chunks`) with automatic sync triggers enables fast full-text search across all code chunks. WAL mode and busy_timeout enabled for concurrent access. Affected: `Database/DbContext.cs`, `Database/DbWriter.cs`.

- **Chunked content storage** — Files are split into 80-line chunks with 10-line overlap between consecutive chunks, enabling granular full-text search with sufficient context at chunk boundaries. Affected: `Indexer/ChunkSplitter.cs`.

- **Regex-based symbol extraction** — Extracts function, class, and import symbols from 13 languages: Python (`def`, `async def`, `class`), JavaScript/TypeScript (`function`, `class`, `import`, `export`), C# (`class`/`interface`/`enum`/`record`/`struct`, methods including `abstract`/`virtual`/`override`), Go (`func`, `type`), Rust (`fn`, `struct`, `enum`, `trait`, `impl`), Java/Kotlin (`class`, methods, `fun`), Ruby (`def`, `class`, `module`), C/C++ (functions, `struct`, `namespace`, `enum`), PHP (`function`, `class`, `interface`, `trait`), Swift (`func`, `class`, `struct`, `enum`, `protocol`). Affected: `Indexer/SymbolExtractor.cs`.

- **Incremental indexing** — Compares file modification timestamps and SHA256 checksums against the database; unchanged files are skipped entirely. Checksum fallback handles cases where timestamps change but content stays the same (e.g. `git checkout`). Affected: `Database/DbWriter.cs`, `Program.cs`.

- **Stale file purging for branch switching** — Automatically detects and removes database entries for files that no longer exist on disk (e.g., after `git checkout` to a different branch). Runs before indexing in incremental mode. Affected: `Database/DbWriter.cs`, `Program.cs`.

- **Batch commit optimization** — Database writes are committed in batches of 500 records per transaction, balancing memory usage and write performance. Affected: `Database/DbWriter.cs`.

- **CLI interface** — Subcommands (`index`, `search`, `symbols`, `files`, `status`) with `--db`, `--rebuild`, `--verbose`, `--json`, `--commits`, `--files` options. Displays progress every 50 files and a summary with file/chunk/symbol counts and elapsed time. Themed spinner easter eggs (`--sushi`, `--coffee`, `--ramen`, etc.). Affected: `Program.cs`, `Cli/ConsoleUi.cs`, `Cli/GitHelper.cs`.

- **FTS5 query sanitization** — User input to FTS5 MATCH is sanitized by quoting each token as a literal phrase, preventing syntax errors from special characters (`*`, `"`, `AND`, `OR`, `NOT`, `NEAR`). Affected: `Database/DbReader.cs`.

- **LIKE query escaping** — `%` and `_` in user queries for `SearchSymbols` and `ListFiles` are properly escaped with `ESCAPE` clause. Affected: `Database/DbReader.cs`.

- **Connection string safety** — Uses `SqliteConnectionStringBuilder` to prevent injection via paths containing `;`. Affected: `Database/DbContext.cs`.

- **Git argument validation** — Commit IDs passed to `git diff-tree` are validated with a regex whitelist and `--` option terminator. Affected: `Cli/GitHelper.cs`.

- **FTS sync via database triggers** — `AFTER INSERT/DELETE/UPDATE` triggers on the `chunks` table automatically keep `fts_chunks` in sync, preventing orphan FTS entries. `CleanExistingFileData()` removes old chunks and symbols before re-upserting. Affected: `Database/DbContext.cs`, `Database/DbWriter.cs`.

- **CLAUDE.md AI search prompt template** — Bilingual (English/Japanese) reference document with ready-to-use SQL queries for path search, full-text search, symbol lookup, language filtering, and file overview. Includes notes on branch switching and database staleness detection. Affected: `CLAUDE.md`.

- **Test suite** — 60 xUnit tests covering ChunkSplitter (6 tests), SymbolExtractor (18 tests), FileIndexer (8 tests), Database integration (14 tests including FTS orphan prevention and checksum-based detection), and DbReader queries (14 tests). Affected: `tests/CodeIndex.Tests/UnitTest1.cs`.

---

## 日本語

### [Unreleased]

#### 追加

- **コアインデックスエンジン** — プロジェクトディレクトリを再帰的に走査し、24言語にわたる33種類のファイル拡張子を検出（Python, JavaScript, TypeScript, C#, Go, Rust, Java, Kotlin, Swift, Ruby, PHP, C/C++, SQL, HTML, CSS, SCSS, Vue, Svelte, Terraform, Shell, Markdown, YAML, JSON, TOML）。一般的な非ソースディレクトリ（`.git`, `node_modules`, `__pycache__`, `venv`, `dist`, `build`, `.next`, `.idea`, `vendor`等）とロックファイル（`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`）をスキップ。対象: `Indexer/FileIndexer.cs`。

- **FTS5全文検索対応SQLiteデータベース** — 3つのコアテーブル（`files`, `chunks`, `symbols`）に言語、更新日時、file_id、シンボル名のインデックスを設定。FTS5仮想テーブル（`fts_chunks`）と自動同期トリガーにより全コードチャンクの高速全文検索が可能。WALモードとbusy_timeoutで並行アクセスに対応。対象: `Database/DbContext.cs`, `Database/DbWriter.cs`。

- **チャンク分割コンテンツ保存** — ファイルを80行ごとに分割し、連続するチャンク間に10行の重複を持たせることで、チャンク境界での十分なコンテキストを保ちつつきめ細かい全文検索を実現。対象: `Indexer/ChunkSplitter.cs`。

- **正規表現によるシンボル抽出** — 13言語から関数、クラス、インポートシンボルを抽出: Python（`def`, `async def`, `class`）、JavaScript/TypeScript（`function`, `class`, `import`, `export`）、C#（`class`/`interface`/`enum`/`record`/`struct`、`abstract`/`virtual`/`override`対応メソッド）、Go（`func`, `type`）、Rust（`fn`, `struct`, `enum`, `trait`, `impl`）、Java/Kotlin（`class`, メソッド, `fun`）、Ruby（`def`, `class`, `module`）、C/C++（関数, `struct`, `namespace`, `enum`）、PHP（`function`, `class`, `interface`, `trait`）、Swift（`func`, `class`, `struct`, `enum`, `protocol`）。対象: `Indexer/SymbolExtractor.cs`。

- **インクリメンタルインデックス** — ファイルの更新日時とSHA256チェックサムをデータベースと比較し、未変更ファイルを完全にスキップ。チェックサムのフォールバックにより、タイムスタンプが変わっても内容が同じ場合（例: `git checkout`）を処理。対象: `Database/DbWriter.cs`, `Program.cs`。

- **ブランチ切り替え対応の古いファイルパージ** — ディスク上に存在しなくなったファイル（例：`git checkout`で別ブランチに切り替え後）のデータベースエントリを自動検出・削除。インクリメンタルモードではインデックス処理前に実行。対象: `Database/DbWriter.cs`, `Program.cs`。

- **バッチコミット最適化** — データベースへの書き込みを1トランザクションあたり500レコードのバッチでコミットし、メモリ使用量と書き込み性能のバランスを最適化。対象: `Database/DbWriter.cs`。

- **CLIインターフェース** — サブコマンド（`index`, `search`, `symbols`, `files`, `status`）と`--db`, `--rebuild`, `--verbose`, `--json`, `--commits`, `--files`オプションに対応。50ファイルごとに進捗を表示し、ファイル数・チャンク数・シンボル数と経過時間のサマリーを出力。テーマ付きスピナーのイースターエッグ（`--sushi`, `--coffee`, `--ramen`等）。対象: `Program.cs`, `Cli/ConsoleUi.cs`, `Cli/GitHelper.cs`。

- **FTS5クエリサニタイズ** — FTS5 MATCHへのユーザー入力を各トークンをリテラルフレーズとして引用しサニタイズ。特殊文字（`*`, `"`, `AND`, `OR`, `NOT`, `NEAR`）による構文エラーを防止。対象: `Database/DbReader.cs`。

- **LIKEクエリエスケープ** — `SearchSymbols`と`ListFiles`のクエリで`%`と`_`を`ESCAPE`句で適切にエスケープ。対象: `Database/DbReader.cs`。

- **接続文字列の安全性** — `SqliteConnectionStringBuilder`を使用し、`;`を含むパスによるインジェクションを防止。対象: `Database/DbContext.cs`。

- **Git引数バリデーション** — `git diff-tree`に渡されるコミットIDを正規表現ホワイトリストで検証し、`--`オプション終端を追加。対象: `Cli/GitHelper.cs`。

- **データベーストリガーによるFTS同期** — `chunks`テーブルの`AFTER INSERT/DELETE/UPDATE`トリガーで`fts_chunks`を自動同期し、FTS孤立エントリを防止。`CleanExistingFileData()`は再UPSERT前に古いチャンクとシンボルを削除。対象: `Database/DbContext.cs`, `Database/DbWriter.cs`。

- **CLAUDE.md AI検索プロンプトテンプレート** — 英語・日本語併記のリファレンスドキュメント。パス検索、全文検索、シンボル検索、言語フィルタリング、ファイル概要の即使用可能なSQLクエリを収録。ブランチ切り替えとデータベースの鮮度検出に関する注記を含む。対象: `CLAUDE.md`。

- **テストスイート** — 60件のxUnitテスト。ChunkSplitter（6件）、SymbolExtractor（18件）、FileIndexer（8件）、Database統合（14件、FTS孤立防止・チェックサム検出含む）、DbReaderクエリ（14件）をカバー。対象: `tests/CodeIndex.Tests/UnitTest1.cs`。
