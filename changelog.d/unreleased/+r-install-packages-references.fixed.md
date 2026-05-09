---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `install.packages()` calls now surface in reference search** — quoted package names, including vector entries, are indexed while named options such as `repos =` are ignored.

## 日本語

- **R の `install.packages()` 呼び出しが参照検索に出るようになりました** — quoted package 名や vector 内の entry を索引し、`repos =` のような named option は無視します。
