---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `data()` calls now surface in reference search** — dataset names and `package =` values are indexed from calls such as `data("iris", package = "datasets")`.

## 日本語

- **R の `data()` 呼び出しが参照検索に出るようになりました** — `data("iris", package = "datasets")` のような呼び出しから dataset 名と `package =` 値を索引します。
