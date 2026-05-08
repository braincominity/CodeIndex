---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift comma-separated enum cases are now indexed individually** — declarations such as `case badRequest, unauthorized, serverError(Int)` emit separate searchable symbols for every case on the line.

## 日本語

- **Swift のカンマ区切り enum case を個別に index するようにしました** — `case badRequest, unauthorized, serverError(Int)` のような宣言で、同じ行の各 case を検索可能な別シンボルとして出力します。
