---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **VB search now ignores `Rem` comments and extracts delegate/operator symbols** — reference extraction strips VB `Rem` comment lines before scanning for calls, `Delegate Sub/Function` declarations are emitted as `delegate` symbols, and `Operator` declarations now show up as searchable `operator` symbols in `symbols`, `definition`, and graph-oriented queries.

## 日本語

- **VB 検索で `Rem` コメントを無視し、Delegate / Operator 宣言を抽出するようになりました** — call/reference 抽出の前に VB の `Rem` コメント行を取り除き、`Delegate Sub/Function` を `delegate` シンボルとして、`Operator` 宣言を `operator` シンボルとして出力するため、`symbols`、`definition`、graph 系クエリで見つけられるようになりました。
