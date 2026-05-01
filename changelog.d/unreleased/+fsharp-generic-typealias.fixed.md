---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# generic type abbreviations and backtick-escaped alias names stay searchable as `typealias`** — `type Result<'T> = Choice<'T, string>` and similar abbreviations now remain visible to `symbols`, `definition`, and `outline`, including aliases that use a `when` constraint or backtick-escaped names.

## 日本語

- **F# の generic type abbreviation とバッククォート付き別名名が `typealias` として検索可能になりました** — `type Result<'T> = Choice<'T, string>` のような省略形が `symbols`、`definition`、`outline` から消えず、`when` 制約やバッククォートでエスケープされた別名も見えるままになります。
