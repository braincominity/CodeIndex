---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran common block member names now appear in reference search** — declarations such as `common /state/ value, flag` index both the block and listed members.

## 日本語

- **Fortran の common block member 名が参照検索に出るようになりました** — `common /state/ value, flag` のような宣言で block 名だけでなく列挙された member も索引します。
