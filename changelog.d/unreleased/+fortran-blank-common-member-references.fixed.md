---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran blank common member names now appear in reference search** — declarations such as `common state, flag` index the listed blank common members.

## 日本語

- **Fortran の blank common member 名が参照検索に出るようになりました** — `common state, flag` のような宣言で列挙された blank common member を索引します。
