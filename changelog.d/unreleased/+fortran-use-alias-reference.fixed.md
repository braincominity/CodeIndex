---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `use only` aliases now appear in reference search** — imports such as `use mod, only: local_name => remote_name` index the local alias as well as the remote symbol.

## 日本語

- **Fortran の `use only` alias が参照検索に出るようになりました** — `use mod, only: local_name => remote_name` のような import で、remote symbol に加えて local alias も索引します。
