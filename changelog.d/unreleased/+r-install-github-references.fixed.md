---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R GitHub package install calls now surface in reference search** — `remotes::install_github()` and `devtools::install_github()` repository specs are indexed while named options are ignored.

## 日本語

- **R の GitHub package install 呼び出しが参照検索に出るようになりました** — `remotes::install_github()` / `devtools::install_github()` の repository spec を索引し、named option は無視します。
