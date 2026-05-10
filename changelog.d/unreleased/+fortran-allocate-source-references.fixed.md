---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `allocate` source and mold arguments now appear in reference search** — simple names on the right-hand side of `source=` and `mold=` are indexed as references.

## 日本語

- **Fortran の `allocate` source/mold 引数が参照検索に出るようになりました** — `source=` と `mold=` の右辺にある simple name を参照として索引します。
