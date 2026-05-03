---
category: fixed
issues:
  - 1357
affected:
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Rust qualified exact-search contract remains leaf-based outside macro invocations (#1357)** — Rust macro invocations keep their existing path-aware exact behavior, while other qualified Rust exact lookups continue to collapse to the leaf name for compatibility. The regression tests now document that contract explicitly.

## 日本語

- **Rust の qualified exact-search 契約は macro 以外では leaf-based のままです (#1357)** — Rust の macro invocation は既存の path-aware exact behavior を維持し、それ以外の qualified Rust exact lookup は互換性のため leaf 名へ畳み込む挙動を継続します。regression test でその契約を明示しました。
