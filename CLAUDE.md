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

# MCP server (for AI tools: Claude Code, Cursor, Windsurf, etc.)
cdidx mcp [--db <path>]
```

## Architecture

```
src/CodeIndex/
  Program.cs               — CLI entry point, subcommand routing, --json support, .git/info/exclude auto-add
  Cli/ConsoleUi.cs         — Spinner, progress bar, banner, easter egg, version, usage text
  Cli/GitHelper.cs         — Git helpers: diff-tree for --commits, worktree-aware common dir resolution
  Database/DbContext.cs     — SQLite connection, schema init (WAL, FTS5, triggers, busy_timeout)
  Database/DbWriter.cs      — UPSERT (ON CONFLICT DO UPDATE), batch insert, stale file purge
  Database/DbReader.cs      — Query operations (FTS search, symbol lookup, file listing, status)
  Indexer/FileIndexer.cs    — Directory scan, language detection, FileRecord building (returns warning via tuple)
  Indexer/ChunkSplitter.cs  — 80-line chunks with 10-line overlap
  Indexer/SymbolExtractor.cs — Regex-based symbol extraction (multi-language)
  Mcp/McpServer.cs          — MCP server (stdin/stdout JSON-RPC 2.0, tools for AI coding tools)
  Models/                   — FileRecord, ChunkRecord, SymbolRecord (plain DTOs)
tests/CodeIndex.Tests/
  ChunkSplitterTests.cs     — ChunkSplitter tests
  SymbolExtractorTests.cs   — SymbolExtractor tests (multi-language)
  FileIndexerTests.cs       — FileIndexer tests (scan, detect, build)
  DatabaseTests.cs          — DbContext/DbWriter integration tests
  DbReaderTests.cs          — DbReader query tests (FTS, symbols, files, status)
  McpServerTests.cs         — MCP server JSON-RPC protocol and tool tests
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
- **No direct Console output from library code** — `FileIndexer.BuildRecord()` returns warnings as a return value `(FileRecord, string, string?)` instead of writing to stderr. The caller (`Program.cs`) handles display, clearing the progress bar line first via `ConsoleUi.ClearProgressLine()`.
- **`.cdidx/` directory** — Index files are stored in `.cdidx/codeindex.db` (not project root). The directory is auto-created on first `cdidx index` and auto-added to `.git/info/exclude` so users don't touch `.gitignore`. In a git worktree, `.git` is a file (not a directory), so `GitHelper.ResolveGitCommonDir()` follows the chain to find the shared `.git/` where `info/exclude` lives. This is a standard Git mechanism (used by git-lfs, Husky, JetBrains IDEs, etc.).

  **Normal repo vs worktree structure:**
  ```
  # Normal repo — .git is a directory, info/exclude is right there
  /projects/my-app/                   ← project root
  ├── 📂 .git/                        ← directory
  │   └── 📂 info/
  │       └── exclude                 ← AddToGitExclude writes here
  └── 📂 .cdidx/
      └── codeindex.db

  # Worktree — .git is a file, need to chase references to find info/exclude
  /projects/my-app/                   ← main repo root
  └── 📂 .git/                        ← actual git directory (shared)
      ├── 📂 info/
      │   └── exclude                 ← AddToGitExclude writes here
      └── 📂 worktrees/
          └── 📂 feature-branch/
              └── commondir           ← contains "../.."

  /projects/my-app-feature/           ← worktree root
  ├── .git                            ← FILE containing "gitdir: /projects/my-app/.git/worktrees/feature-branch"
  └── 📂 .cdidx/
      └── codeindex.db
  ```

  **Resolution chain in worktree:**
  1. Read `.git` file → `gitdir: /projects/my-app/.git/worktrees/feature-branch`
  2. Read `commondir` file at that path → `../..`
  3. Resolve `../..` relative to `feature-branch/` dir:
     `feature-branch/` → `..` → `worktrees/` → `..` → `.git/`
  4. Write to `.git/info/exclude`

## Conventions

- Comments are bilingual (English / Japanese), e.g. `// Enable WAL mode / WALモードを有効化`
- Documentation (README, CHANGELOG) is structured: English first, then Japanese.
- No unnecessary packages — `System.CommandLine` was removed in favor of manual arg parsing.

## Rules for changes (important)

### Method signature changes
When changing a method's return type or parameters (e.g. `BuildRecord` from `(FileRecord, string)` to `(FileRecord, string, string?)`), **update ALL callers** in the same commit:
- `Program.cs` (main indexing loop AND `--commits`/`--files` update mode)
- `Mcp/McpServer.cs`
- `tests/CodeIndex.Tests/` (use `_` to discard unused elements)

### Console output and progress bar
The progress bar uses `\r` without newline. On Windows, stdout and stderr share the cursor position. **Any output (WARN, ERR, verbose [OK]/[SKIP]) while the progress bar is active MUST call `ConsoleUi.ClearProgressLine()` first**, or the message will merge with the bar on the same line.

### Easter egg themes
Features that exist in the spinner (braille frames, themed emoji+text) must carry through to the progress bar. `ConsoleUi.SetProgressTheme()` reuses frames from `GetSpinnerFrames()` — don't duplicate the frame definitions.

### Per-commit checklist
Before every commit, check whether each of the following needs updating. Don't batch these up — evaluate and act on each commit:
1. **Tests** — Does this change break existing tests or require new ones? Search for affected method/class names in `tests/`.
2. **CHANGELOG.md** — Does this change deserve an entry? Update both English and Japanese sections.
3. **README.md** — Does this change affect user-facing behavior, CLI options, defaults, or examples? Update both English and Japanese sections.
4. **DEVELOPER_GUIDE.md** — Does this change affect architecture, design decisions, or AI integration guidance?
5. **CLAUDE.md** — Does this change affect architecture, design decisions, or development rules?
6. **PR description** — Does this commit change the scope of the PR? Update the title/description to reflect the final state.

### Documentation — keep in sync
The following files contain overlapping content that must be updated together:
- **README.md** — English section AND Japanese section (both must match)
- **DEVELOPER_GUIDE.md** — References README for the CLAUDE.md template and exit codes. Has its own design decisions and architecture sections.
- **CHANGELOG.md** — English section AND Japanese section
- **CLAUDE.md** — This file; update architecture/design sections when code changes

When modifying the CLAUDE.md template (code search rules for AI agents), update both instances in README (English and Japanese). DEVELOPER_GUIDE references README, so no separate update is needed there.

### CHANGELOG style
- One entry per distinct change. Don't merge unrelated fixes into a single entry just to reduce line count.
- But don't write separate entries for iterative commits toward the same fix — consolidate them into one entry describing the final result.
- Use [Keep a Changelog](https://keepachangelog.com/) categories: Added, Changed, Fixed, Removed.
- Each entry: `**Bold title** — Description. Affected: \`file1\`, \`file2\`.`

### Pull requests
- Title and description in **English**.
- Structure: `## Summary` (bullet points grouped by theme) + `## Test plan` (checkbox list).
- When iterating on a PR, update the title/description to reflect the final state, not the history of changes.

### Tests
When changing public API signatures or adding new public methods, check if tests need updating. Run `dotnet test` to verify. If the build environment lacks .NET SDK, at minimum verify all callers are updated by searching for the method name.

### README structure
- Section numbering must be consistent (don't have "2." without "1.").
- Instructions specific to one install method (e.g. PATH setup for build-from-source) belong under that method's section, not at the top level.
- Keep explanations simple and factual. Avoid over-explaining edge cases that are unlikely in practice.

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

# MCPサーバー（AIツール向け: Claude Code, Cursor, Windsurf等）
cdidx mcp [--db <path>]
```

## アーキテクチャ

```
src/CodeIndex/
  Program.cs               — CLIエントリポイント、サブコマンドルーティング、--jsonサポート、.git/info/exclude自動追加
  Cli/ConsoleUi.cs         — スピナー、プログレスバー、バナー、イースターエッグ、バージョン、使い方
  Cli/GitHelper.cs         — --commitsオプション用のgit diff-treeヘルパー
  Database/DbContext.cs     — SQLite接続、スキーマ初期化（WAL, FTS5, トリガー, busy_timeout）
  Database/DbWriter.cs      — UPSERT（ON CONFLICT DO UPDATE）、バッチ挿入、古いファイルのパージ
  Database/DbReader.cs      — クエリ操作（FTS検索、シンボル検索、ファイル一覧、ステータス）
  Indexer/FileIndexer.cs    — ディレクトリ走査、言語検出、FileRecord構築（警告をタプルで返す）
  Indexer/ChunkSplitter.cs  — 80行チャンク（10行重複）
  Indexer/SymbolExtractor.cs — 正規表現によるシンボル抽出（多言語対応）
  Mcp/McpServer.cs          — MCPサーバー（stdin/stdout JSON-RPC 2.0、AIツール向けツール公開）
  Models/                   — FileRecord, ChunkRecord, SymbolRecord（プレーンDTO）
tests/CodeIndex.Tests/
  ChunkSplitterTests.cs     — ChunkSplitterテスト
  SymbolExtractorTests.cs   — SymbolExtractorテスト（多言語対応）
  FileIndexerTests.cs       — FileIndexerテスト（走査、検出、構築）
  DatabaseTests.cs          — DbContext/DbWriter統合テスト
  DbReaderTests.cs          — DbReaderクエリテスト（FTS、シンボル、ファイル、ステータス）
  McpServerTests.cs         — MCPサーバーJSON-RPCプロトコル・ツールテスト
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
- **ライブラリコードから直接Console出力しない** — `FileIndexer.BuildRecord()`は警告を戻り値`(FileRecord, string, string?)`で返す。表示は呼び出し元（`Program.cs`）が`ConsoleUi.ClearProgressLine()`でプログレスバーをクリアしてから行う。
- **`.cdidx/`ディレクトリ** — インデックスファイルは`.cdidx/codeindex.db`に格納（プロジェクトルート直下ではない）。初回の`cdidx index`でディレクトリを自動作成し、`.git/info/exclude`に自動追加するためユーザーが`.gitignore`を編集する必要なし。git worktreeでは`.git`がディレクトリではなくファイルのため、`GitHelper.ResolveGitCommonDir()`で解決チェーンを辿って`info/exclude`がある共通`.git/`を見つける。Git標準の仕組み（git-lfs、Husky、JetBrains IDE等が利用）。

  **通常リポジトリ vs worktreeの構造:**
  ```
  # 通常リポジトリ — .gitがディレクトリ、info/excludeはその直下
  /projects/my-app/                   ← プロジェクトルート
  ├── 📂 .git/                        ← ディレクトリ
  │   └── 📂 info/
  │       └── exclude                 ← AddToGitExcludeがここに書き込む
  └── 📂 .cdidx/
      └── codeindex.db

  # worktree — .gitがファイル、参照を辿ってinfo/excludeを見つける
  /projects/my-app/                   ← 元リポジトリのルート
  └── 📂 .git/                        ← 実体のgitディレクトリ（共有）
      ├── 📂 info/
      │   └── exclude                 ← AddToGitExcludeがここに書き込む
      └── 📂 worktrees/
          └── 📂 feature-branch/
              └── commondir           ← "../.."が入っている

  /projects/my-app-feature/           ← worktreeのルート
  ├── .git                            ← ファイル。中身は "gitdir: /projects/my-app/.git/worktrees/feature-branch"
  └── 📂 .cdidx/
      └── codeindex.db
  ```

  **worktreeでの解決チェーン:**
  1. `.git`ファイルを読む → `gitdir: /projects/my-app/.git/worktrees/feature-branch`
  2. そのパスの`commondir`ファイルを読む → `../..`
  3. `../..`を`feature-branch/`ディレクトリ起点で解決:
     `feature-branch/` → `..` → `worktrees/` → `..` → `.git/`
  4. `.git/info/exclude`に書き込む

## コーディング規約

- コメントは英日併記（例: `// Enable WAL mode / WALモードを有効化`）
- ドキュメント（README, CHANGELOG）は前半英語、後半日本語の構成。
- 不要なパッケージは入れない — `System.CommandLine`は手動引数解析に置き換えて削除済み。

## 変更時のルール（重要）

### メソッドシグネチャの変更
メソッドの戻り値やパラメータを変更した場合（例: `BuildRecord`を`(FileRecord, string)`から`(FileRecord, string, string?)`に変更）、**同じコミットで全ての呼び出し元を更新すること**:
- `Program.cs`（メインのインデックスループ AND `--commits`/`--files`更新モード）
- `Mcp/McpServer.cs`
- `tests/CodeIndex.Tests/`（不要な要素は`_`で破棄）

### コンソール出力とプログレスバー
プログレスバーは`\r`（改行なし）で出力する。Windowsではstdoutとstderrがカーソル位置を共有する。**プログレスバー表示中に何かを出力する場合（WARN、ERR、verbose [OK]/[SKIP]）は必ず先に`ConsoleUi.ClearProgressLine()`を呼ぶこと**。そうしないとメッセージがバーと同じ行に結合される。

### イースターエッグテーマ
スピナーに存在する機能（ブレイルフレーム、テーマ付き絵文字＋テキスト）はプログレスバーにも反映すること。`ConsoleUi.SetProgressTheme()`は`GetSpinnerFrames()`のフレームを再利用する — フレーム定義を重複させないこと。

### コミットごとのチェックリスト
コミットのたびに、以下の各項目について更新要否を判断すること。後回しにせず、各コミット単位で確認・対応する:
1. **テスト** — この変更で既存テストが壊れないか？新規テストが必要か？`tests/` 内で影響を受けるメソッド・クラス名を検索。
2. **CHANGELOG.md** — この変更はエントリに値するか？英語・日本語の両セクションを更新。
3. **README.md** — ユーザー向けの動作、CLIオプション、デフォルト値、使用例に影響するか？英語・日本語の両セクションを更新。
4. **DEVELOPER_GUIDE.md** — アーキテクチャ、設計判断、AI連携ガイドに影響するか？
5. **CLAUDE.md** — アーキテクチャ、設計判断、開発ルールに影響するか？
6. **PR説明** — このコミットでPRのスコープが変わったか？タイトル・説明を最終状態に合わせて更新。

### ドキュメント — 同期を保つ
以下のファイルには重複する内容があり、同時に更新する必要がある:
- **README.md** — 英語セクション AND 日本語セクション（両方一致させる）
- **DEVELOPER_GUIDE.md** — CLAUDE.mdテンプレートと終了コードはREADMEを参照。設計判断・アーキテクチャは独自セクション。
- **CHANGELOG.md** — 英語セクション AND 日本語セクション
- **CLAUDE.md** — このファイル。コード変更時にアーキテクチャ・設計セクションも更新

CLAUDE.mdテンプレート（AI向けコード検索ルール）を変更する場合、READMEの両インスタンス（英語・日本語）を更新すること。DEVELOPER_GUIDEはREADMEを参照しているため個別の更新は不要。

### CHANGELOGのスタイル
- 変更ごとに1エントリ。無関係な修正を行数削減のために1エントリにまとめない。
- ただし、同じ修正に向けた段階的なコミットは1エントリに統合し、最終結果を記述する。
- [Keep a Changelog](https://keepachangelog.com/)のカテゴリを使用: Added, Changed, Fixed, Removed（日本語: 追加, 変更, 修正, 削除）。
- 各エントリ: `**太字タイトル** — 説明。Affected: \`file1\`, \`file2\`.`（日本語: `対象:`）

### プルリクエスト
- タイトルと説明は**英語**で書く。
- 構成: `## Summary`（テーマ別の箇条書き）+ `## Test plan`（チェックボックスリスト）。
- PRを修正していく過程で、タイトル・説明は変更履歴ではなく**最終状態**を反映するよう更新する。

### テスト
公開APIのシグネチャ変更や新しい公開メソッド追加時はテストの更新要否を確認する。`dotnet test`で検証。ビルド環境に.NET SDKがない場合でも、最低限メソッド名を検索して全呼び出し元が更新されていることを確認する。

### READMEの構成
- セクション番号は一貫させる（「1.」なしに「2.」を書かない）。
- 特定のインストール方法に固有の手順（ビルド時のPATH設定等）はそのセクション内に置き、トップレベルに出さない。
- 説明はシンプルかつ事実ベースに。実際に起こりにくいエッジケースを過剰に説明しない。
