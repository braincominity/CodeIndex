---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Rust `use` trees now index across line breaks** — symbol extraction now reads multi-line Rust `use` statements before splitting the tree, so formatted imports like `use std::{ ... }` and `pub(crate) use crate::{ ... }` surface the same import symbols as single-line forms.

## 日本語

- **Rust の `use` tree を改行またぎで索引するようになりました** — symbol extraction が複数行の Rust `use` 文を先にまとめてから tree を分解するため、`use std::{ ... }` や `pub(crate) use crate::{ ... }` のような整形済み import でも 1 行版と同じ import symbol が出るようになりました。
