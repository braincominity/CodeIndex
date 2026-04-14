# Maintainers & Forkers

> **[日本語版はこちら / Japanese version](#maintainer-と-forker-向け)**

This index lists the documents and sections that are **only relevant to the
repository owner, maintainers, or forkers** — not to end users who simply
`cdidx index` their codebase. They cover releasing, CI/install plumbing, and
the AI-driven self-improvement workflow on this specific repo.

If you are an end user looking for usage docs, you can ignore everything on
this page — `README.md` is enough.

## What's on this page for

- **Releasing a new version of cdidx.** Only the owner has release push
  permissions, but a forker bumping their own fork needs the same steps.
  → [DEVELOPER_GUIDE.md → "Release Workflow"](DEVELOPER_GUIDE.md#release-workflow)
- **Bootstrapping a Claude Code cloud session with no local .NET SDK.** Only
  useful to someone who wants to run Claude Code *against this repo* from a
  SDK-less container (owner workflow; forkers can reuse the same prompt).
  → [CLOUD_BOOTSTRAP_PROMPT.md](CLOUD_BOOTSTRAP_PROMPT.md) — drop-in first-turn prompt.
  → [DEVELOPER_GUIDE.md → "Cloud Claude Code bootstrap (no .NET SDK)"](DEVELOPER_GUIDE.md#cloud-claude-code-bootstrap-no-net-sdk) — deep dive on the install/runtime mechanics behind the prompt.
- **AI-driven self-improvement loop.** The operating contract used by
  maintainer-run Claude Code sessions to iterate on cdidx itself. End users
  shouldn't need this; forkers may adapt it.
  → [SELF_IMPROVEMENT.md](SELF_IMPROVEMENT.md)

## Why these are separated

The linked documents stay in their original locations so existing links and
search paths keep working. This page just flags them as *not part of the
end-user documentation surface* so that:

- End users don't waste time reading release / CI internals.
- Maintainers and forkers have one entry point to everything operational.
- New maintainer-facing docs have an obvious home to get linked from.

---

# Maintainer と forker 向け

このページは、**このリポジトリの Maintainer または forker にのみ
関係する**ドキュメントとセクションの索引です。単に自分のコードベースを
`cdidx index` したいエンドユーザーには不要な情報です。リリース、CI と
インストールの裏側、およびこのリポジトリ固有の AI 駆動自己改善フローを扱います。

使い方を知りたいだけのエンドユーザーはこのページを無視して構いません。
`README.md` だけで十分です。

## このページが扱う範囲

- **cdidx の新バージョンリリース。** リリースの push 権限を持つのは Maintainer だけですが、fork して自分のリリースを切る人にも同じ手順が必要です。
  → [DEVELOPER_GUIDE.md → 「リリース手順」](DEVELOPER_GUIDE.md#リリース手順)
- **.NET SDK のないコンテナから Claude Code Cloud セッションを bootstrap する。** SDK の無いコンテナから *このリポジトリ* に対して Claude Code を走らせたい人（Maintainer のワークフロー。forker も同じプロンプトを流用可）に限って有用。
  → [CLOUD_BOOTSTRAP_PROMPT.md](CLOUD_BOOTSTRAP_PROMPT.md) — 初回投入用のプロンプト。
  → [DEVELOPER_GUIDE.md → 「Cloud Claude Code bootstrap（.NET SDK なし）」](DEVELOPER_GUIDE.md#cloud-claude-code-bootstrapnet-sdk-なし) — そのプロンプトの裏で走るインストール・ランタイムの詳細解説。
- **AI 駆動の自己改善ループ。** Maintainer が走らせる Claude Code セッションが cdidx 自身を改善するときの運用契約。エンドユーザーには不要。forker は改変して使える。
  → [SELF_IMPROVEMENT.md](SELF_IMPROVEMENT.md)

## なぜ分離するのか

リンクと検索導線を壊さないため、各ドキュメントは現在の場所から動かしません。
このページは単に「**エンドユーザー向けドキュメントの範囲外**」という旗を
立てる役割を担います:

- エンドユーザーがリリース内部や CI 内部の情報を読んで時間を無駄にしない。
- Maintainer と forker に、運用系ドキュメントの単一の入口を提供する。
- 今後 maintainer 向けドキュメントを足すときの、明示的なリンク元になる。
