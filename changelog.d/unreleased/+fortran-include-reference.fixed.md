---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/FortranReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `include` targets now appear in reference search** — quoted include paths such as `include 'constants.inc'` are indexed as references so included source dependencies can be found.

## 日本語

- **Fortran の `include` 対象が参照検索に出るようになりました** — `include 'constants.inc'` のような引用付き include path を参照として索引し、取り込み元ソースの依存を見つけられるようにしました。
