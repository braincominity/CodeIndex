---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R Shiny bracket-style output renderers now surface in symbol search** — `output[["detail-plot"]] <- renderPlot(...)` is indexed by output id.

## 日本語

- **R Shiny の bracket 形式 output renderer がシンボル検索に出るようになりました** — `output[["detail-plot"]] <- renderPlot(...)` を output id で索引します。
