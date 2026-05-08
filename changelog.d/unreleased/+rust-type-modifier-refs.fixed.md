---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust type modifiers no longer appear as type references** — keywords such as `impl`, `dyn`, `const`, `mut`, and the `'static` lifetime are filtered from type-reference results while preserving the real surrounding types.

## 日本語

- **Rust の型 modifier が type reference として出ないようになりました** — `impl`、`dyn`、`const`、`mut`、`'static` lifetime などのキーワードを型参照結果から除外しつつ、周辺の実型は保持します。
