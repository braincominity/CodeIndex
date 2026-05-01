---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Rust `use` re-exports and imported leaf names are now indexed** — symbol extraction now records `pub use` / `pub(crate) use` statements and the imported leaf or alias names inside `use` trees, so searches can find re-exported Rust symbols directly.

## 日本語

- **Rust の `use` re-export と import 先の leaf 名もインデックスされるようになりました** — `pub use` / `pub(crate) use` と `use` tree の中の leaf 名や alias 名もシンボル抽出で記録するため、re-export された Rust シンボルを直接検索できるようになります。
