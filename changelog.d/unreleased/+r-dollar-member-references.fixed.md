---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `$` member accesses now surface in reference search** — expressions such as `input$go`, `data$value`, and backtick member names emit both qualified and leaf references.

## 日本語

- **R の `$` member access が参照検索に出るようになりました** — `input$go`、`data$value`、バッククォート付き member 名から qualified / leaf の両方の参照を記録します。
