---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `source()` call references are retained alongside sourced file paths** — dynamic `source(path_var)` usages remain searchable by call while static paths also appear as references.

## 日本語

- **R の `source()` call 参照を source 先ファイルパスと併せて維持するようになりました** — `source(path_var)` のような動的な利用も call として検索でき、静的パスも参照として記録します。
