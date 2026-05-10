---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran namelist member names now appear in reference search** — declarations such as `namelist /config/ value, status` index both the group and listed members.

## 日本語

- **Fortran の namelist member 名が参照検索に出るようになりました** — `namelist /config/ value, status` のような宣言で group 名だけでなく列挙された member も索引します。
