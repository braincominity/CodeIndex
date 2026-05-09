---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R roxygen `@import` tags now surface in reference search** — package-level roxygen imports such as `@import ggplot2 dplyr` are indexed as package references.

## 日本語

- **R の roxygen `@import` タグが参照検索に出るようになりました** — `@import ggplot2 dplyr` のような package-level import を package reference として索引します。
