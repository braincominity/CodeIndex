---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran pointer assignment targets now appear in reference search** — assignments such as `callback => resolver` index the target name while ignoring `null()` resets.

## 日本語

- **Fortran の pointer assignment target が参照検索に出るようになりました** — `callback => resolver` のような代入で target 名を索引し、`null()` への reset は除外します。
