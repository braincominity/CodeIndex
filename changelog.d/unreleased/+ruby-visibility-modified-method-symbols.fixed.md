---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby visibility-modified method definitions are now indexed** — `cdidx` recognizes `private def`, `protected def`, and `public def` forms, including singleton methods.

## 日本語

- **Ruby の visibility modifier 付きメソッド定義を索引するようになりました** — `cdidx` は singleton method を含む `private def`、`protected def`、`public def` 形式を認識します。
