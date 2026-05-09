---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R roxygen `@importFrom` tags now surface in reference search** — `@importFrom`, `@importMethodsFrom`, and `@importClassesFrom` comments emit package-qualified and leaf references.

## 日本語

- **R の roxygen `@importFrom` タグが参照検索に出るようになりました** — `@importFrom`、`@importMethodsFrom`、`@importClassesFrom` コメントから package-qualified / leaf の両方の参照を記録します。
