---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R Shiny output renderers now surface in symbol search** — `output$plot <- renderPlot(...)` and related renderers are indexed by output id.

## 日本語

- **R Shiny の output renderer がシンボル検索に出るようになりました** — `output$plot <- renderPlot(...)` などの renderer を output id で索引します。
