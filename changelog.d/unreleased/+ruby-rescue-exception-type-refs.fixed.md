---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `rescue` clauses now index exception types** — `cdidx` records exception constants in `rescue ErrorType` clauses so searches can find handlers for Ruby error classes.

## 日本語

- **Ruby の `rescue` 節が例外型を索引するようになりました** — `cdidx` は `rescue ErrorType` 節の例外定数を記録するため、Ruby のエラークラスを扱うハンドラを検索で見つけられます。
