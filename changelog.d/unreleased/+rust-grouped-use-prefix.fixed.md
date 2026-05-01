---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **Rust grouped `use` trees now keep their path prefixes** — symbol extraction now records the full qualified path for grouped imports like `use std::collections::{HashMap, HashSet}` in addition to the imported leaf names, so Rust searches can match both the prefix and the imported item.

## 日本語

- **Rust の grouped `use` tree で path prefix も保持するようになりました** — `use std::collections::{HashMap, HashSet}` のような grouped import について、import 先の leaf 名に加えて完全修飾 path も symbol 抽出で記録するため、Rust 検索で prefix と import 名の両方にマッチできるようになります。
