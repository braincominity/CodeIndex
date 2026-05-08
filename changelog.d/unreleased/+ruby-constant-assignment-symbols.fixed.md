---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby constant assignments are now indexed as property symbols** — `cdidx` records constants such as `MAX_RETRIES = 3`, making Ruby configuration and domain constants searchable through symbol queries.

## 日本語

- **Ruby の定数代入を property シンボルとして索引するようになりました** — `cdidx` は `MAX_RETRIES = 3` のような定数を記録するため、Ruby の設定値やドメイン定数をシンボル検索で見つけられます。
