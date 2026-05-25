# cdidx

> **[日本語版はこちら / Japanese version](#cdidx日本語)**

[![Build and Test](https://github.com/Widthdom/CodeIndex/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/dotnet.yml)
[![CodeQL](https://github.com/Widthdom/CodeIndex/actions/workflows/codeql.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/codeql.yml)
[![Release](https://github.com/Widthdom/CodeIndex/actions/workflows/release.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/release.yml)

![.NET 8.x / 9.x tests](https://img.shields.io/badge/.NET-8.x%20%2F%209.x%20tests-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/License-FSL--1.1--ALv2-orange)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white)

**CLI code indexing and MCP search for local repositories.**

`cdidx` is a command-line code indexer and MCP server that builds a local
SQLite index of your repository so humans and AI agents can run fast
full-text, symbol, dependency, and inspection queries without repeatedly
rescanning the same tree.

## Why cdidx

> **Index once. Ask many times.** `cdidx` turns a repository into a local
> retrieval runtime, so humans and AI agents can pull scoped code context
> without rescanning or resending broad files every turn.

| If your workflow is... | Best fit | Why |
|---|---|---|
| One-off string hunting | `rg` | zero setup, direct file scan |
| Repeated repository investigation | `cdidx` | local SQLite FTS5 index, structured results, incremental refresh |
| VS Code-only chat context | VS Code workspace index | editor-managed context inside the Copilot/VS Code UX |
| Terminal, CI, scripts, or MCP clients | `cdidx` | explicit CLI + MCP boundary that works outside an IDE |

Details: [why cdidx](USER_GUIDE.md#why-cdidx), [cdidx vs rg](USER_GUIDE.md#cdidx-vs-rg),
and [cdidx vs VS Code workspace index](USER_GUIDE.md#cdidx-vs-vs-code-workspace-index).

## Quick Start

```bash
# Install is usually seconds. Homebrew is available on macOS/Linux:
brew install widthdom/tap/codeindex

# Or install as a .NET global tool from NuGet:
dotnet tool install -g cdidx

# Or install directly from the signed GitHub release assets:
curl -fsSL https://raw.githubusercontent.com/Widthdom/CodeIndex/main/install.sh | bash

# First index: ~30-60s on small repos; minutes or longer on 100k-file trees.
# Add --verbose to see each file status while it runs.
cdidx .
cdidx status --check --json
cdidx search "handleRequest"
cdidx definition UserService
cdidx search "Handle" --project MyApp
cdidx mcp
```

After the first command, use these cues and follow-up commands:

| Situation | What to expect or run |
|---|---|
| Index progress | The terminal shows `Scanning...`, `Indexing...`, a `67.0% [28/42]`-style progress line, and an elapsed time such as `2.4s` or `5m 42s`. |
| Edits or branch switches | Refresh incrementally with `--files`, `--commits`, or `--changed-between <old-ref> <new-ref>` instead of rebuilding. See [Quick Start](USER_GUIDE.md#quick-start) and [Incremental update reliability](USER_GUIDE.md#incremental-update-reliability). |
| Intentional rebuilds | Interactive terminals ask before deleting the DB. Scripts and CI must pass `--yes` or `--force`. |
| Long-lived DB compaction | Run `cdidx optimize` or `cdidx index <projectPath> --optimize` to compact FTS5 segments immediately. Incremental refreshes also optimize opportunistically. |
| Permission or I/O scan errors | `cdidx` records the scan error, continues other directories, and writes `.cdidx/scan-checkpoint.json` so same-HEAD retries can skip completed directories. |

Output controls:

| Need | Option |
|---|---|
| Owner-only persistent stderr logs on POSIX | Global tool stderr logs are forced to `0600` permissions on every open, including existing date-stamped log files. |
| ASCII-only terminal output | Use `--ascii`, `CDIDX_ASCII=1`, `NO_UNICODE`, `TERM=dumb`, accessibility env hints, or a non-UTF-8 locale. Spinners use pipe, slash, dash, and backslash frames; progress bars use `#` / `-`; very narrow terminals fall back to percentage-only progress. |
| Color and terminal capability | `--color auto` emits ANSI only for capable interactive terminals; `TERM=dumb`, `CI=true`, missing Unix terminal hints, `NO_COLOR`, or `CLICOLOR=0` disable ANSI/progress control sequences. `--palette basic|256|truecolor` can override the `COLORTERM` / `TERM` color-depth detection. |
| UTF-8 JSON pipelines | CLI `--json` output is written as UTF-8 without a BOM and never includes ANSI escape sequences, even when color is forced for human output. |
| Script-friendly query pipelines | Use `--quiet`, `-q`, `--silent`, or `CDIDX_QUIET=1` to suppress informational stderr text while preserving errors. `--quiet` takes precedence over `--verbose`. |

Use `cdidx` when a repository will be searched repeatedly from terminals,
scripts, CI, or AI tools. Use `rg` when you only need a one-off text scan.

### Shell Completion

Generate completion scripts with `cdidx --completions <bash|zsh|fish|powershell>`.
The generated scripts complete subcommands, flags, and common flag values:
`--lang` suggests supported languages, `--kind` suggests symbol/reference
kinds, and path-like options such as `--db`, `--path`, and `--output` use shell
file completion.

## Highlights

| Area | What cdidx provides |
|---|---|
| Search surfaces | CLI-first output for humans and machines; full-text, symbol, reference, caller/callee, dependency, map, inspect, and excerpt commands. |
| Ranking and filters | Public/exported symbol matches rank ahead of protected, internal, and private matches. Use `--no-visibility-rank` for legacy order, and `--visibility` / `--exclude-visibility` with `symbols`, `definition`, `unused`, and `hotspots`. |
| Project scoping | `.sln` / `.csproj`-aware <code>--project &lt;name&#124;path&gt;</code> filters for indexing and queries, plus `--solution <path>` when a workspace has multiple solution files. |
| MCP integration | MCP server support for AI clients such as Claude Code, Cursor, and Windsurf, including tools, indexed-file resources, starter prompts, and `Language support:` descriptions sourced from the same registries as `cdidx languages`. |
| Freshness | Parallel full-scan extraction with `--parallelism`, incremental refreshes with `--files` and `--commits`, continuous `--watch`, exact `status --check`, and configurable stale thresholds via `--stale-after` / `CDIDX_STALE_AFTER`. |
| Storage | Local-first `.cdidx/codeindex.db` storage. `--data-dir <dir>`, `CDIDX_DATA_DIR`, or `XDG_DATA_HOME` can move default SQLite storage outside the workspace; explicit `--db <path>` still wins. |
| DB maintenance | New indexes use SQLite incremental auto-vacuum. `cdidx vacuum` reclaims free pages from existing DBs, including a one-time full `VACUUM` conversion for legacy no-autovacuum DBs, and `status --json` reports metrics under `db_pragma_settings`. |
| Security defaults | On POSIX systems, `.cdidx` is created with `0700` permissions and `status --json` reports the effective `data_dir_mode` when available. |
| Diagnostics | `status --explain <field>` describes readiness fields and remediation. Read commands support `--profile`, `--slow-query-ms <n>`, and <code>--trace=stderr&#124;file&#124;none</code>; file traces write daily `query-trace-YYYYMMDD.jsonl` files next to the lifecycle log. |
| Query exit codes | Valid zero-result query commands exit `0` by default. Pass `--strict-not-found` when scripts should treat zero rows as exit code `2`. |
| Drift checks | `cdidx diff <db1> <db2>` compares schema, file, symbol, and reference deltas with stable exit codes: `0` identical, `1` drift, `2` schema mismatch, `3` unreadable DB. |
| Extensibility and feedback | Post-extraction hooks from `~/.config/cdidx/hooks/*.dll` or `CDIDX_HOOKS_DIR` can enrich symbols and references. `cdidx suggestions` lists, inspects, and exports local suggestion history, with fuzzy MCP suggestion deduplication controlled by CLI, env, or `.cdidxrc.json`. |
| Language coverage | 78 detected languages, with symbol and graph support where available. |
| Updates | `cdidx --version` checks GitHub releases at most once per day and appends a newer-release hint when one is available. Set `CDIDX_DISABLE_UPDATE_CHECK=1` to suppress the check. |

The documented `status --json` trust contract covers these fields:

<table>
<tbody>
<tr><td><code>fold_ready</code></td><td><code>fold_ready_reason</code></td><td><code>graph_table_available</code></td><td><code>issues_table_available</code></td></tr>
<tr><td><code>sql_graph_contract_ready</code></td><td><code>sql_graph_contract_degraded_reason</code></td><td><code>hotspot_family_ready</code></td><td><code>hotspot_family_degraded_reason</code></td></tr>
<tr><td><code>csharp_symbol_name_ready</code></td><td><code>csharp_metadata_target_ready</code></td><td><code>csharp_metadata_target_degraded_reason</code></td><td><code>indexed_head_commit</code></td></tr>
<tr><td><code>worktree_head_changed</code></td><td><code>indexed_head_sha</code></td><td><code>indexed_head_branch</code></td><td><code>indexed_head_timestamp</code></td></tr>
<tr><td><code>commits_ahead_of_indexed_head</code></td><td><code>index_writer_version</code></td><td><code>index_newer_than_reader</code></td><td><code>index_newer_than_reader_reason</code></td></tr>
<tr><td><code>unknown_extension_file_count</code></td><td><code>path_case_sensitive</code></td><td><code>data_dir</code></td><td><code>data_dir_source</code></td></tr>
<tr><td><code>data_dir_mode</code></td><td><code>mac_profile</code></td><td><code>db_pragma_settings</code></td><td><code>hooks</code></td></tr>
<tr><td><code>stale_after_seconds</code></td><td><code>index_age_seconds</code></td><td><code>degraded_reason</code></td><td><code>recommended_action</code></td></tr>
<tr><td><code>alternative_action</code></td><td><code>mcp_session</code></td><td></td><td></td></tr>
</tbody>
</table>

For MCP `status`, `mcp_session` is session-scoped diagnostic data rather than persisted index state. It includes `log_level`, `roots`, and optional `client_capabilities`.

`hotspot_family_degraded_reason` uses these values:

| Value | Meaning |
|---|---|
| `hotspot_family_support_not_indexed` | Legacy DB without hotspot-family support. |
| `hotspot_family_metadata_stale` | Hotspot-family metadata is stale. |
| `hotspot_family_disabled_at_index_time` | The index was written while marker fingerprints were unavailable. |

Hotspot-family readiness is tracked by per-language
`hotspot_family_version_<lang>` metadata introduced with hotspot-family contract
version 2.

## Documentation

| Document | Contents |
|---|---|
| [User Guide](USER_GUIDE.md) | Detailed installation, command examples, options, supported languages, MCP setup, and troubleshooting. |
| [Cloud Bootstrap](CLOUD_BOOTSTRAP_PROMPT.md) | Install guidance for restricted cloud agent sessions. |
| [Platform Support](docs/platform-support.md) | Official release asset RIDs, unsupported platforms, and source-build alternatives. |
| [Developer Guide](DEVELOPER_GUIDE.md) | Architecture, implementation notes, release workflow, and the [`reference_kind` filtering matrix](DEVELOPER_GUIDE.md#reference-kind-filtering-matrix) for `callers` / `impact` / `deps` count reconciliation. |
| [Testing Guide](TESTING_GUIDE.md) | Test conventions and validation commands. |
| [Agent Guide](AGENT_GUIDE.md) | Shared agent entry point, workflow index, search policy, and status contract maintenance rules. |
| [Claude Code Entry Point](CLAUDE.md) | Thin Claude Code entry point that redirects to the shared agent guide. |
| [Self-Improvement Contract](SELF_IMPROVEMENT.md) | Rules for agents improving CodeIndex itself. |
| [Integration Policy](INTEGRATION_POLICY.md) | Permitted CLI, JSON, MCP, and integration use. |
| [Security Policy](SECURITY.md) | Private vulnerability reporting and coordinated disclosure policy. |
| [Code of Conduct](CODE_OF_CONDUCT.md) | Community standards and reporting expectations. |

## Supported Surfaces

`cdidx` is a **CLI and MCP server** only. The supported, versioned surfaces are
the `cdidx` CLI (including its `--json` output) and the `cdidx mcp` JSON-RPC
interface. There is no public library / SDK API: the `cdidx` NuGet package is
published as a .NET global tool (`PackAsTool=true`), and `public` types on the
assembly are implementation details that may change without notice. See
[INTEGRATION_POLICY.md — API Surface and Library Use](INTEGRATION_POLICY.md#api-surface-and-library-use).

## Verifying releases

Every GitHub release ships the following supply-chain artifacts next to
the per-platform `CodeIndex-<rid>.tar.gz` / `.zip` binaries:

| Asset | Purpose |
|---|---|
| `sha256sums.txt` | SHA-256 of every release asset (including the SBOM). `install.sh` uses it to verify the downloaded tarball before placing anything under `$HOME/.local/bin/`. |
| `cdidx.sbom.cdx.json` | CycloneDX 1.x JSON Software Bill of Materials covering every NuGet dependency (including the bundled `SQLitePCLRaw` native asset) so compliance reviewers (SOC2, FedRAMP-style) and scanners (Snyk, Trivy, Grype) can audit transitive dependencies without re-deriving them from `.deps.json`. |

Quick check after downloading both files from the release page:

```bash
sha256sum --check sha256sums.txt --ignore-missing
jq '.metadata.component.name, .components | length' cdidx.sbom.cdx.json
```

## License and Fair Source Use

CodeIndex and official `cdidx` binaries are source-available / Fair Source-style software
under [FSL-1.1-ALv2](LICENSE), unless a specific file or directory says
otherwise. Integration materials may be [Apache-2.0](LICENSES/Apache-2.0.txt)
where marked.

See [COMMERCIAL_LICENSE.md](COMMERCIAL_LICENSE.md), [INTEGRATION_POLICY.md](INTEGRATION_POLICY.md),
and [TRADEMARKS.md](TRADEMARKS.md) for commercial, integration, and naming
details.

# cdidx（日本語）

[![Build and Test](https://github.com/Widthdom/CodeIndex/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/dotnet.yml)
[![CodeQL](https://github.com/Widthdom/CodeIndex/actions/workflows/codeql.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/codeql.yml)
[![Release](https://github.com/Widthdom/CodeIndex/actions/workflows/release.yml/badge.svg)](https://github.com/Widthdom/CodeIndex/actions/workflows/release.yml)

![.NET 8.x / 9.x tests](https://img.shields.io/badge/.NET-8.x%20%2F%209.x%20tests-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/License-FSL--1.1--ALv2-orange)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white)

**ローカルリポジトリ向けの CLI コードインデックスと MCP 検索です。**

`cdidx` はコマンドラインのコードインデクサー兼 MCP サーバーで、リポジトリの
ローカル SQLite index を作成します。人間と AI エージェントは、同じツリーを
何度も読み直さずに、高速な全文検索、シンボル、依存関係、inspect クエリを
実行できます。

## なぜ cdidx なのか

> **一度インデックスして、何度も聞く。** `cdidx` はリポジトリをローカルな
> retrieval runtime に変え、人間と AI エージェントが毎回広いファイルを
> 読み直さずに、必要なコード文脈だけを取り出せるようにします。

| ワークフロー | 向いているもの | 理由 |
|---|---|---|
| 1回限りの文字列探し | `rg` | セットアップ不要で直接ファイルを読む |
| 同じリポジトリの反復調査 | `cdidx` | SQLite FTS5 のローカル index、構造化結果、差分更新 |
| VS Code 内だけの chat 文脈 | VS Code workspace index | Copilot / VS Code UX 内で editor が管理 |
| ターミナル、CI、スクリプト、MCP client | `cdidx` | IDE 外でも使える明示的な CLI + MCP 境界 |

詳細: [なぜ cdidx なのか](USER_GUIDE.md#なぜ-cdidx-なのか)、[rg との違い](USER_GUIDE.md#rg-との違い)、
[VS Code workspace index との違い](USER_GUIDE.md#vs-code-workspace-index-との違い)。

## すぐに試す

```bash
# インストールは通常数秒で終わります。Homebrew は macOS/Linux で利用できます。
brew install widthdom/tap/codeindex

# または NuGet から .NET global tool としてインストールできます。
dotnet tool install -g cdidx

# または署名付き GitHub release asset から直接インストールできます。
curl -fsSL https://raw.githubusercontent.com/Widthdom/CodeIndex/main/install.sh | bash

# 初回 index は小規模 repo で約30-60秒、100kファイル級では数分以上かかることがあります。
# 実行中のファイル別ステータスを見たい場合は --verbose を付けてください。
cdidx .
cdidx status --check --json
cdidx search "handleRequest"
cdidx definition UserService
cdidx search "Handle" --project MyApp
cdidx mcp
```

初回実行後は、次の見方と追加コマンドをよく使います。

| 状況 | 見るもの / 使うもの |
|---|---|
| index の進捗 | `Scanning...`、`Indexing...`、`67.0% [28/42]` のような進捗行と、`2.4s` や `5m 42s` のような単位付き経過時間が表示されます。 |
| 編集後やブランチ切り替え後 | 再構築ではなく `--files`、`--commits`、`--changed-between <old-ref> <new-ref>` で差分更新します。詳細は [クイックスタート](USER_GUIDE.md#クイックスタート) と [インクリメンタル更新の信頼性](USER_GUIDE.md#インクリメンタル更新の信頼性) を参照してください。 |
| 意図的な再構築 | interactive terminal では既存 DB 削除前に確認を求めます。script / CI では `--yes` または `--force` が必要です。 |
| 長期間使っている DB の compact | `cdidx optimize` または `cdidx index <projectPath> --optimize` で FTS5 segment をすぐに compact できます。差分更新中も必要に応じて自動 optimize します。 |
| 権限や I/O の scan error | `cdidx` は scan error を記録し、他のディレクトリの走査を続けます。同じ HEAD の再実行では `.cdidx/scan-checkpoint.json` により成功済みディレクトリを読み飛ばせます。 |

出力を整える option:

| 目的 | option / 動作 |
|---|---|
| POSIX の persistent stderr log を owner-only にする | global tool stderr log は開くたびに `0600` 権限へ補正され、既存の日付付き log file も同じ扱いになります。 |
| ASCII-only 端末で崩れない表示にする | `--ascii`、`CDIDX_ASCII=1`、`NO_UNICODE`、`TERM=dumb`、accessibility 系の環境変数、非 UTF-8 locale を使います。スピナーは pipe、slash、dash、backslash の frame、進捗バーは `#` / `-` になり、幅が非常に狭い端末では percentage-only になります。 |
| color と端末 capability | `--color auto` は対応する interactive terminal でだけ ANSI を出力します。`TERM=dumb`、`CI=true`、Unix で端末 hint が無い場合、`NO_COLOR`、`CLICOLOR=0` では ANSI / progress 制御シーケンスを抑止します。`--palette basic|256|truecolor` で `COLORTERM` / `TERM` による color-depth 判定を上書きできます。 |
| UTF-8 JSON pipeline | CLI の `--json` 出力は BOM なし UTF-8 で書き出され、human output 向けに色を強制していても ANSI escape sequence を含みません。 |
| script 向け query pipeline の stderr を静かにする | `--quiet`、`-q`、`--silent`、`CDIDX_QUIET=1` で informational stderr を抑制し、error 行だけを残します。`--quiet` は `--verbose` より優先されます。 |

ターミナル、スクリプト、CI、AI ツールから同じリポジトリを繰り返し検索する
場合は `cdidx` が向いています。1回限りのテキスト検索には `rg` が向いています。

### シェル補完

`cdidx --completions <bash|zsh|fish|powershell>` で補完スクリプトを生成できます。
生成されたスクリプトは subcommand、flag、よく使う flag 値を補完します。
`--lang` は対応言語、`--kind` は symbol / reference kind を提示し、`--db`、
`--path`、`--output` など path 系 option は shell の file completion を使います。

## 特長

| 分野 | 内容 |
|---|---|
| 検索面 | CLI-first の人間向け / 機械処理向け出力。全文検索、シンボル、参照、caller/callee、依存関係、map、inspect、excerpt コマンドを提供します。 |
| 順位と filter | public/exported なシンボル一致を protected、internal、private より優先します。従来順は `--no-visibility-rank`、可視性の include / exclude は `symbols`、`definition`、`unused`、`hotspots` の `--visibility` / `--exclude-visibility` で指定できます。 |
| project scope | `.sln` / `.csproj` を使った <code>--project &lt;name&#124;path&gt;</code> filter で index と query を .NET project 配下へ絞り込めます。workspace に solution が複数ある場合は `--solution <path>` を指定します。 |
| MCP 連携 | Claude Code、Cursor、Windsurf などの AI クライアント向け MCP server。tools、インデックス済みファイル resources、starter prompts、`cdidx languages` と同じ言語レジストリ由来の `Language support:` 説明を提供します。 |
| freshness | `--parallelism` による parallel full-scan、`--files` / `--commits` による差分更新、`--watch` による継続更新、`status --check` による完全一致確認、`--stale-after` / `CDIDX_STALE_AFTER` による age threshold 上書きに対応します。 |
| storage | `.cdidx/codeindex.db` に保存する local-first 設計。既定の SQLite 保存先は `--data-dir <dir>`、`CDIDX_DATA_DIR`、`XDG_DATA_HOME` で workspace 外へ移せます。明示的な `--db <path>` は引き続き最優先です。 |
| DB maintenance | 新規 index DB は SQLite incremental auto-vacuum を使います。既存 DB は `cdidx vacuum` で free page を回収でき、legacy no-autovacuum DB は初回だけ full `VACUUM` で変換します。`status --json` は `db_pragma_settings` 配下に metrics を出力します。 |
| security defaults | POSIX では `.cdidx` を `0700` 権限で作成します。`status --json` は利用可能な場合に実効 POSIX mode を `data_dir_mode` として報告します。 |
| diagnostics | `status --explain <field>` は readiness field の意味と対処を説明します。read 系コマンドは `--profile`、`--slow-query-ms <n>`、<code>--trace=stderr&#124;file&#124;none</code> に対応し、file trace は lifecycle log と同じ場所に日次 `query-trace-YYYYMMDD.jsonl` を書きます。 |
| drift checks | `cdidx diff <db1> <db2>` は schema、file、symbol、reference の差分を比較します。exit code は `0` identical、`1` drift、`2` schema mismatch、`3` unreadable DB です。 |
| extensibility / feedback | `~/.config/cdidx/hooks/*.dll` または `CDIDX_HOOKS_DIR` の post-extraction hook で永続化前のシンボルと参照を拡張できます。`cdidx suggestions` はローカル提案履歴の一覧表示、詳細表示、エクスポートに対応し、MCP 提案の近似重複排除しきい値は CLI、env、`.cdidxrc.json` で調整できます。 |
| language coverage | 78 言語を検出し、対応言語ではシンボルとグラフも利用可能です。 |
| updates | `cdidx --version` は GitHub releases を 1 日 1 回まで確認し、新しいリリースがある場合にヒントを追記します。確認を抑止するには `CDIDX_DISABLE_UPDATE_CHECK=1` を設定します。 |

文書化された `status --json` trust contract は次の field を対象にします。

<table>
<tbody>
<tr><td><code>fold_ready</code></td><td><code>fold_ready_reason</code></td><td><code>graph_table_available</code></td><td><code>issues_table_available</code></td></tr>
<tr><td><code>sql_graph_contract_ready</code></td><td><code>sql_graph_contract_degraded_reason</code></td><td><code>hotspot_family_ready</code></td><td><code>hotspot_family_degraded_reason</code></td></tr>
<tr><td><code>csharp_symbol_name_ready</code></td><td><code>csharp_metadata_target_ready</code></td><td><code>csharp_metadata_target_degraded_reason</code></td><td><code>indexed_head_commit</code></td></tr>
<tr><td><code>worktree_head_changed</code></td><td><code>indexed_head_sha</code></td><td><code>indexed_head_branch</code></td><td><code>indexed_head_timestamp</code></td></tr>
<tr><td><code>commits_ahead_of_indexed_head</code></td><td><code>index_writer_version</code></td><td><code>index_newer_than_reader</code></td><td><code>index_newer_than_reader_reason</code></td></tr>
<tr><td><code>unknown_extension_file_count</code></td><td><code>path_case_sensitive</code></td><td><code>data_dir</code></td><td><code>data_dir_source</code></td></tr>
<tr><td><code>data_dir_mode</code></td><td><code>mac_profile</code></td><td><code>db_pragma_settings</code></td><td><code>hooks</code></td></tr>
<tr><td><code>stale_after_seconds</code></td><td><code>index_age_seconds</code></td><td><code>degraded_reason</code></td><td><code>recommended_action</code></td></tr>
<tr><td><code>alternative_action</code></td><td><code>mcp_session</code></td><td></td><td></td></tr>
</tbody>
</table>

MCP `status` の `mcp_session` は永続化された index 状態ではなく、セッション単位の診断情報です。`log_level`、`roots`、任意の `client_capabilities` を含みます。

`hotspot_family_degraded_reason` は次の値を使います。

| 値 | 意味 |
|---|---|
| `hotspot_family_support_not_indexed` | hotspot-family 未対応の legacy DB。 |
| `hotspot_family_metadata_stale` | hotspot-family metadata が古い状態。 |
| `hotspot_family_disabled_at_index_time` | marker fingerprint が利用できない状態で書かれた index。 |

hotspot-family readiness は、hotspot-family contract version 2 で導入された
言語別 `hotspot_family_version_<lang>` metadata で追跡されます。

## ドキュメント

| ドキュメント | 内容 |
|---|---|
| [ユーザーガイド](USER_GUIDE.md#cdidx日本語) | 詳細なインストール、コマンド例、オプション、対応言語、MCP 設定、トラブルシュート。 |
| [クラウドブートストラップ](CLOUD_BOOTSTRAP_PROMPT.md#日本語) | 制限されたクラウドエージェント環境でのインストール手順。 |
| [プラットフォームサポート](docs/platform-support.md#プラットフォームサポート) | 公式リリースアセットの RID、未対応 platform、source build の代替手段。 |
| [開発者ガイド](DEVELOPER_GUIDE.md#開発者ガイド) | アーキテクチャ、実装メモ、リリース手順、`callers` / `impact` / `deps` の件数差を照合する [`reference_kind` フィルタの対応表](DEVELOPER_GUIDE.md#reference-kind-filtering-matrix)。 |
| [テストガイド](TESTING_GUIDE.md#テストガイド) | テスト規約と検証コマンド。 |
| [エージェントガイド](AGENT_GUIDE.md) | 共有エージェント入口、workflow index、検索ポリシー、status contract の保守ルール。 |
| [Claude Code 入口](CLAUDE.md) | 共有エージェントガイドへリダイレクトする薄い Claude Code entry point。 |
| [自己改善コントラクト](SELF_IMPROVEMENT.md#自己改善ループ) | CodeIndex 自身を改善するエージェント向けルール。 |
| [統合ポリシー](INTEGRATION_POLICY.md) | CLI、JSON、MCP、各種統合で許可される利用。 |
| [セキュリティポリシー](SECURITY.md) | 非公開の脆弱性報告と協調的開示の方針。 |
| [行動規範](CODE_OF_CONDUCT.md) | コミュニティ標準と報告時の期待事項。 |

## サポート対象の利用面

`cdidx` は **CLI と MCP サーバーとしてのみ** 提供します。バージョニング契約の
対象となるのは、`cdidx` CLI（`--json` 出力を含む）と `cdidx mcp` の JSON-RPC
インターフェースだけです。公開ライブラリ/SDK API は提供していません。`cdidx`
NuGet パッケージは .NET グローバルツールとして公開されており
（`PackAsTool=true`）、アセンブリ上の `public` 型は CLI/MCP の実装上の事情で
公開されているだけの実装詳細で、予告なく変更される可能性があります。詳細は
[INTEGRATION_POLICY.md — API Surface and Library Use](INTEGRATION_POLICY.md#api-surface-and-library-use)
を参照してください。

## リリース成果物の検証

各 GitHub release では、プラットフォーム別の
`CodeIndex-<rid>.tar.gz` / `.zip` バイナリと並んで、サプライチェーン関連の
成果物を同梱しています。

| アセット | 用途 |
|---|---|
| `sha256sums.txt` | 各リリースアセット（SBOM を含む）の SHA-256。`install.sh` は `$HOME/.local/bin/` に何も書き込む前に tarball をこのファイルで検証します。 |
| `cdidx.sbom.cdx.json` | CycloneDX 1.x JSON 形式の Software Bill of Materials。同梱の `SQLitePCLRaw` ネイティブアセットを含む全 NuGet 依存を列挙するため、SOC2 / FedRAMP 系のコンプライアンスレビューや Snyk / Trivy / Grype などのスキャナーが `.deps.json` から再構築せずに推移的依存を監査できます。 |

リリースページから両ファイルをダウンロードしたあとの簡易チェック例:

```bash
sha256sum --check sha256sums.txt --ignore-missing
jq '.metadata.component.name, .components | length' cdidx.sbom.cdx.json
```

## ライセンスと Fair Source の扱い

CodeIndex と公式 `cdidx` バイナリは、ファイルやディレクトリで別途明記されない
限り [FSL-1.1-ALv2](LICENSE) の source-available / Fair Source-style software
です。統合用の素材は、明記されている場合 [Apache-2.0](LICENSES/Apache-2.0.txt)
で利用できます。

商用利用、統合、名称の扱いについては [COMMERCIAL_LICENSE.md](COMMERCIAL_LICENSE.md)、
[INTEGRATION_POLICY.md](INTEGRATION_POLICY.md)、[TRADEMARKS.md](TRADEMARKS.md) を
参照してください。
