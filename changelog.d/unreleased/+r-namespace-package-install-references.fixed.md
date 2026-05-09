---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `renv::install()` and `pak::pkg_install()` calls now surface in reference search** — quoted package names and vector entries are indexed like `install.packages()`.

## 日本語

- **R の `renv::install()` / `pak::pkg_install()` 呼び出しが参照検索に出るようになりました** — quoted package 名や vector 内の entry を `install.packages()` と同様に索引します。
