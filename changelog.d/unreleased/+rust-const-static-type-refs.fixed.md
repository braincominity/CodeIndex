---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust `const` and `static` item types are now indexed as type references** — declarations such as `const GLOBAL: Arc<User>` and `static mut STATE: Option<State>` now contribute their annotated types to reference search and inspection workflows.

## 日本語

- **Rust の `const` / `static` item の型を type reference としてインデックスするようになりました** — `const GLOBAL: Arc<User>` や `static mut STATE: Option<State>` のような宣言で、注釈された型を参照検索や inspect ワークフローから辿れます。
