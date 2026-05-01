---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **VB search now ignores `Rem` comments and extracts delegate symbols** — reference extraction strips VB `Rem` comment lines before scanning for calls, and `Delegate Sub/Function` declarations are now emitted as `delegate` symbols so they show up in `symbols`, `definition`, and graph-oriented queries.

## 日本語

- **VB 検索で `Rem` コメントを無視し、Delegate 宣言を抽出するようになりました** — call/reference 抽出の前に VB の `Rem` コメント行を取り除き、`Delegate Sub/Function` 宣言を `delegate` シンボルとして出力するため、`symbols`、`definition`、graph 系クエリで見つけられるようになりました。
