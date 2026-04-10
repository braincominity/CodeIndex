# Self-Improvement Loop

> **[日本語版はこちら / Japanese version](#自己改善ループ)**

This document is a ready-to-use operating contract for AI agents improving `cdidx` from inside this repository.

Use it when the task is:
- improve `cdidx` itself
- identify missing AI-facing features or branding gaps
- implement non-breaking improvements immediately
- repeat the loop commit by commit while keeping the local index fresh

## Goal

Keep making `cdidx` more useful to:
- AI agents
- human developers using AI tools
- terminal-first users
- MCP-based coding workflows

The loop is not just "suggest ideas". It is:
1. inspect the current product with `cdidx`
2. identify the next high-value gap
3. implement it if it is non-breaking
4. verify it
5. commit it
6. rebuild `cdidx`
7. refresh `.cdidx/codeindex.db` with the newly built binary
8. use the refreshed index to guide the next improvement

## Hard Rules

- Create a work branch first. Use a descriptive branch name.
- Keep **one task per commit**.
- Before every commit, explicitly work through the `CLAUDE.md` per-commit checklist.
- Before every commit, review README `# Code Search Rules` and `# コードベース検索ルール`; strengthen them if AI behavior should change.
- After every commit, rebuild `cdidx` from the latest local source and refresh `.cdidx/codeindex.db` using that freshly built binary.
- Prefer the **locally built latest binary** (`dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll`) over an older globally installed `cdidx` whenever the repository code has changed. **Never fall back to a global `cdidx`** — the global version may have an older DB schema, missing query features, or stale extraction logic that silently produces wrong results.
- After `git reset`, `git rebase`, `git commit --amend`, `git switch`, or `git merge`, re-index with the **locally built binary** using `dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll .` (full scan, not `--commits HEAD`) so stale files are purged against the current checkout.
- When searching and navigating code to investigate bugs, plan fixes, or verify changes, always use the **locally built binary** — not the globally installed version. This ensures query results reflect the latest extraction rules and DB schema from this branch.
- If a change may be breaking, migration-heavy, destructive, or likely to impose manual work on users, **stop and ask for approval before implementing**.
- Respect language differences. Do not pretend every query type is meaningful for every language.
- Respect platform differences. Do not assume Windows, macOS, and Linux behave the same for paths, file locking, process invocation, or cleanup.
- Favor implementation over brainstorming when the next improvement is clear and non-breaking.
- Keep docs and tests in sync with behavior.
- Do not push tags or branches unless explicitly asked.

## Breaking-Change Gate

You must ask the user before proceeding if the next change would do any of the following:
- change DB layout in a way that could break older indexes or require forced rebuilds
- remove or rename CLI/MCP behavior in a user-visible way
- lower compatibility with older databases without a safe fallback
- require users to change their workflow, config, or prompts manually
- introduce risky migrations or destructive cleanup

If a DB/schema change is necessary, design the read path so newer binaries do not crash on older layouts. Prefer:
- additive columns
- additive tables
- opportunistic migration on open
- safe fallback reads when in-place migration is not available

## Standard Loop

### 1. Create a branch

Create and switch to a descriptive work branch before making changes.

Example:

```bash
git switch -c codex/ai-snippets
```

### 2. Build the current source first

Always compile the current repository version before relying on searches for self-improvement work.

```bash
dotnet build
```

### 3. Refresh the index with the freshly built binary

Use the binary produced from the current commit, not an older global tool, so the database shape and query features match the code you are editing.

```bash
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll . --json
```

If you prefer `dotnet run`, that is also acceptable:

```bash
dotnet run --project src/CodeIndex -- . --json
```

### 4. Explore the repo with cdidx itself

Use `cdidx` as your primary navigation tool. Prefer structured and low-token queries first.

Typical sequence:

```bash
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll status --json
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll map --json --limit 10 --path src/ --exclude-tests
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll inspect QueryCommandRunner --exclude-tests --limit 5
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll search "AI" --path src/ --exclude-tests --snippet-lines 6 --json
```

Use `inspect` when you already have a likely symbol name. Use `search` for raw text or unsupported languages. Use `map` first when you need orientation.

### 5. Make a plan before editing

Before implementation, write a concrete plan that covers:
- what gap you found
- why it matters for AI or product adoption
- whether the change is non-breaking
- which files and tests are affected
- how you will verify it
- how language-specific behavior should differ
- how platform-specific behavior should differ, if paths/processes/filesystem semantics are involved

### 6. Implement immediately if safe

If the change is clearly non-breaking and high-value, implement it without waiting for more approval.

Examples:
- better ranking
- better snippets
- improved MCP tool output
- new additive CLI/MCP queries
- better docs/examples/branding
- safer backward-compatible schema additions
- stronger language-aware guards and messaging

### 7. Verify after each implementation

At minimum, do the checks that match the change:
- `dotnet test`
- targeted CLI smoke checks
- MCP behavior checks if MCP was touched
- documentation spot-checks
- language-specific behavior checks if logic differs by language
- platform-sensitive checks if behavior depends on files, paths, processes, console I/O, or SQLite cleanup

Examples:

```bash
dotnet test
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll status --json
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll map --json --path src/ --exclude-tests
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll inspect ResolveGitCommonDir --json --exclude-tests --limit 5
```

### 8. Commit exactly one task

Before committing, explicitly review:
1. Tests
2. CHANGELOG.md
3. README.md
4. README `# Code Search Rules` / `# コードベース検索ルール`
5. DEVELOPER_GUIDE.md
6. CLAUDE.md
7. This file (`SELF_IMPROVEMENT.md`)
8. PR description, if a PR already exists

Then commit one coherent task.

### 9. Rebuild and refresh again after the commit

This is mandatory. The next round must start from the newest binary and newest DB.

```bash
dotnet build
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll . --json
```

### 10. Use the refreshed index to choose the next task

After each commit:
- inspect what the new binary can do
- search for newly exposed gaps
- repeat

## Language-Aware Guidance

Do not use one search strategy for every language.

- `references`, `callers`, and `callees` are meaningful only for languages where cdidx intentionally supports regex-based graph extraction.
- For unsupported languages, prefer `search`, `excerpt`, `definition`, or `files` instead of assuming graph data exists.
- Treat C#, Java, Go, Rust, TypeScript/JavaScript, Python, Kotlin, Ruby, C/C++, PHP, and Swift differently from Markdown, YAML, JSON, TOML, Shell, SQL, HTML/CSS, Vue, Svelte, and Terraform.
- When proposing new language-specific features, state clearly which languages are in scope and why.
- When a heuristic is language-specific, document the limitation in README and tests.

## Platform-Aware Guidance

Do not assume path handling, process cleanup, or file deletion behaves the same on every OS.

- Windows can hold SQLite files longer because of file locking and connection pooling, so cleanup code and tests must tolerate delayed release.
- Path separators, casing assumptions, shell commands, and process launch behavior differ across Windows, macOS, and Linux.
- If you change temp-file handling, DB lifecycle, or CLI process behavior, add verification that is robust across supported platforms.
- If a workaround is OS-specific, document why it exists instead of leaving it as unexplained test fragility.

## Product and Branding Lens

Self-improvement is not limited to search mechanics. Also evaluate:
- positioning versus `rg`, desktop search tools, and IDE search
- first-run clarity
- README opening clarity
- examples that AI agents will actually copy
- MCP discoverability
- names of commands and docs sections
- whether a new user can understand the value in 30 seconds

Good improvements are often:
- fewer round-trips
- lower token output
- clearer defaults
- better trust/freshness signals
- safer compatibility behavior
- sharper product framing

## Suggested Prompt

If a user wants to start this loop, the minimal instruction can be:

```markdown
Read `SELF_IMPROVEMENT.md` and start implementing the next non-breaking improvement.
```

If the user wants more direction:

```markdown
Read `SELF_IMPROVEMENT.md`, inspect the current repo with cdidx itself, identify the next high-value non-breaking improvement for AI friendliness or adoption, implement it, verify it, commit exactly one task, rebuild cdidx from the latest commit, refresh `.cdidx/codeindex.db`, and continue from the refreshed index. Ask before any breaking change.
```

---

<a id="自己改善ループ"></a>
# 自己改善ループ

このドキュメントは、このリポジトリの中から `cdidx` 自身を改善していく AI エージェント向けの、そのまま使える運用契約です。

次のようなタスクで使います:
- `cdidx` 自体を改善したい
- AI向け機能やブランディング上の欠けを見つけたい
- 非破壊な改善はすぐ実装したい
- ローカルインデックスを常に新鮮に保ちながら、コミット単位で改善を回したい

## 目的

`cdidx` を次の相手にとって、もっと役立つものにし続けることです:
- AIエージェント
- AIツールを使う人間の開発者
- ターミナル中心のユーザー
- MCPベースのコーディングワークフロー

このループは「アイデアを出すだけ」ではありません。流れは次のとおりです:
1. `cdidx` 自身で現状を観察する
2. 次に改善すべき価値の高いギャップを見つける
3. 非破壊なら実装する
4. 検証する
5. コミットする
6. `cdidx` を再ビルドする
7. その新しいバイナリで `.cdidx/codeindex.db` を更新する
8. 更新済みインデックスを使って次の改善を決める

## 絶対ルール

- まず作業ブランチを切る。名前は内容が分かるものにする。
- **1案件1コミット** を守る。
- 毎コミット前に、`CLAUDE.md` の「コミットごとのチェックリスト」を明示的に確認する。
- 毎コミット前に、README の `# Code Search Rules` と `# コードベース検索ルール` を見直し、AIの検索行動を変えるべきなら強化する。
- 毎コミット後に、ローカルソースの最新状態から `cdidx` を再ビルドし、その新しいバイナリで `.cdidx/codeindex.db` を更新する。
- リポジトリのコードを変更した後は、古いグローバルインストール版ではなく **ローカルでビルドした最新版バイナリ** (`dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll`) を使う。**グローバル版には絶対に戻らないこと** — グローバル版は DB スキーマが古い、クエリ機能が欠けている、抽出ロジックが古くて誤った結果を返す、といった問題が起こりうる。
- `git reset`、`git rebase`、`git commit --amend`、`git switch`、`git merge` の後は、**ローカルビルド版** で `dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll .`（フルスキャン。`--commits HEAD` ではない）を実行し、現在の checkout に対する stale file を掃除する。
- バグ調査、修正計画、変更検証のためにコード検索・ナビゲーションを行うときも、常に **ローカルビルド版** を使う。グローバルインストール版は使わない。これにより、このブランチの最新の抽出ルールと DB スキーマを反映した検索結果が得られる。
- 変更が破壊的、移行負荷が高い、危険、またはユーザーに手間を強いる可能性があるなら、**実装前に必ず承認を取る**。
- 言語差分を無視しない。すべての言語で同じ検索が意味を持つと仮定しない。
- 次の改善が明確で非破壊なら、議論だけで止まらず実装を優先する。
- ドキュメントとテストを挙動と同期させる。
- `git push` や `git tag` は、明示的に依頼されたときだけ行う。

## 破壊的変更のゲート

次にやろうとしている変更が以下に当てはまるなら、先にユーザーへ確認してください:
- DBレイアウト変更で古いインデックスを壊す、または強制再構築を招く可能性がある
- CLI/MCPのユーザー向け挙動を削除・改名・互換性低下させる
- 古いDBへの互換性を安全なフォールバックなしに下げる
- ユーザー側にワークフローや設定やプロンプトの手修正を要求する
- 危険な移行や破壊的なクリーンアップを伴う

DB/スキーマ変更が必要な場合は、新しいバイナリが古いレイアウトでクラッシュしない読み取り経路を設計してください。優先すべきなのは:
- 追加カラム
- 追加テーブル
- open 時の機会的移行
- その場移行できない場合の安全なフォールバック読み取り

## 標準ループ

### 1. ブランチを切る

変更前に、内容が分かる作業ブランチを作って切り替えます。

例:

```bash
git switch -c codex/ai-snippets
```

### 2. まず現在のソースをビルドする

自己改善の検索に入る前に、必ず現在のリポジトリ版をコンパイルしてください。

```bash
dotnet build
```

### 3. そのビルド済みバイナリでインデックスを更新する

編集中のコードと DB 形状と検索機能を一致させるため、古いグローバルツールではなく、**現在のコミットからビルドしたバイナリ** を使います。

```bash
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll . --json
```

`dotnet run` でも構いません:

```bash
dotnet run --project src/CodeIndex -- . --json
```

### 4. cdidx 自身でリポジトリを観察する

主なナビゲーション手段は `cdidx` にしてください。まずは構造化された、低トークンの問い合わせを優先します。

典型例:

```bash
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll status --json
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll map --json --limit 10 --path src/ --exclude-tests
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll inspect QueryCommandRunner --exclude-tests --limit 5
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll search "AI" --path src/ --exclude-tests --snippet-lines 6 --json
```

候補シンボル名が分かっているなら `inspect`、生テキストや未対応言語なら `search`、全体像が欲しいなら `map` を優先します。

### 5. 編集前に計画を立てる

実装前に、次を含む具体的な計画を立ててください:
- どんなギャップを見つけたか
- それがAIや普及にとってなぜ重要か
- その変更が非破壊かどうか
- 影響ファイルと必要テスト
- どう検証するか
- 言語ごとの差をどう扱うか

### 6. 安全ならすぐ実装する

変更が明らかに非破壊で価値が高いなら、追加承認を待たずに実装します。

例:
- ランキング改善
- スニペット改善
- MCPツール出力改善
- 追加的なCLI/MCPクエリ
- ドキュメント、例、ブランディングの改善
- 後方互換を保ったスキーマ追加
- 言語差分を踏まえたガードやメッセージの改善

### 7. 実装ごとに検証する

変更に応じて、最低限次を実施します:
- `dotnet test`
- CLI のスモーク確認
- MCP を触ったなら MCP 挙動確認
- ドキュメントの spot check
- 言語差分があるなら、言語別の確認

例:

```bash
dotnet test
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll status --json
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll map --json --path src/ --exclude-tests
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll inspect ResolveGitCommonDir --json --exclude-tests --limit 5
```

### 8. 1案件だけコミットする

コミット前に、明示的に次を確認します:
1. Tests
2. CHANGELOG.md
3. README.md
4. README `# Code Search Rules` / `# コードベース検索ルール`
5. DEVELOPER_GUIDE.md
6. CLAUDE.md
7. このファイル（`SELF_IMPROVEMENT.md`）
8. 既存PRがあるなら PR説明

そのうえで、1つのまとまりだけをコミットします。

### 9. コミット後にもう一度ビルドして更新する

これは必須です。次のラウンドは、必ず最新バイナリと最新DBから始めてください。

```bash
dotnet build
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll . --json
```

### 10. 更新済みインデックスから次の案件を選ぶ

各コミット後に:
- 新しいバイナリで何ができるか観察する
- 新たに見えるギャップを探す
- 繰り返す

## 言語差分を前提にする指針

すべての言語で同じ検索戦略を使ってはいけません。

- `references`、`callers`、`callees` は、cdidx が意図的に正規表現ベースのグラフ抽出をサポートしている言語でのみ意味があります。
- 未対応言語では、グラフ結果がある前提で進めず、`search`、`excerpt`、`definition`、`files` を優先します。
- C#、Java、Go、Rust、TypeScript/JavaScript、Python、Kotlin、Ruby、C/C++、PHP、Swift と、Markdown、YAML、JSON、TOML、Shell、SQL、HTML/CSS、Vue、Svelte、Terraform は分けて考えてください。
- 新しい言語依存機能を提案するときは、どの言語を対象にするのか、その理由を明記してください。
- ヒューリスティックが言語依存なら、README とテストに制限事項を残してください。

## プラットフォーム差分を前提にする指針

パス処理、プロセス後始末、ファイル削除がすべての OS で同じだと考えてはいけません。

- Windows では SQLite の接続プールやファイルロックにより、DB ファイル解放が遅れることがあるため、後片付けコードやテストは遅延解放に耐える必要があります。
- パス区切り、大小文字前提、shell コマンド、プロセス起動挙動は Windows、macOS、Linux で異なります。
- 一時ファイル処理、DB ライフサイクル、CLI プロセス挙動を変える場合は、対応プラットフォーム全体で壊れにくい検証を追加してください。
- OS 固有の回避策を入れる場合は、説明のない不安定テストにせず、なぜ必要かをドキュメントに残してください。

## プロダクトとブランディングの観点

自己改善は検索機能だけに限りません。次も評価対象です:
- `rg`、デスクトップ検索、IDE検索に対する位置づけ
- 初回体験の分かりやすさ
- README 冒頭の伝わりやすさ
- AI が本当にコピペする例になっているか
- MCP の見つけやすさ
- コマンド名やドキュメント見出しの分かりやすさ
- 新規ユーザーが30秒で価値を理解できるか

良い改善は、たいてい次のどれかです:
- 往復回数が減る
- トークン出力が減る
- デフォルトが明快になる
- 信頼性や鮮度のシグナルが増える
- 互換性面が安全になる
- プロダクトの打ち出しが鋭くなる

## 推奨プロンプト

このループを始めるとき、最小の指示はこれで十分です:

```markdown
Read `SELF_IMPROVEMENT.md` and start implementing the next non-breaking improvement.
```

少し具体化したいなら:

```markdown
Read `SELF_IMPROVEMENT.md`, inspect the current repo with cdidx itself, identify the next high-value non-breaking improvement for AI friendliness or adoption, implement it, verify it, commit exactly one task, rebuild cdidx from the latest commit, refresh `.cdidx/codeindex.db`, and continue from the refreshed index. Ask before any breaking change.
```
