---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran namelist group names now appear in reference search** — declarations such as `namelist /repository_config/ value` index the named namelist group.

## 日本語

- **Fortran の namelist group 名が参照検索に出るようになりました** — `namelist /repository_config/ value` のような宣言で、名前付き namelist group を索引します。
