---
category: fixed
issues:
  - 1357
affected:
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Rust qualified exact-search now preserves the full symbol path for exact symbol lookups (#1357)** — Qualified Rust symbol queries now match against the qualified container path instead of collapsing to the leaf name first, so exact searches disambiguate same-named symbols in different modules. The regression test covers the path-aware contract.

## 日本語

- **Rust の qualified exact-search は exact symbol lookup で完全な symbol path を保つようになりました (#1357)** — qualified Rust symbol query は leaf 名へ畳み込むのではなく、qualified container path に対して照合されるため、異なる module にある同名 symbol を exact search で区別できます。regression test で path-aware 契約を確認しています。
