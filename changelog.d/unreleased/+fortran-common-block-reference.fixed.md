---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran common block names now appear in reference search** — declarations such as `common /repository_state/ value` index the named common block.

## 日本語

- **Fortran の common block 名が参照検索に出るようになりました** — `common /repository_state/ value` のような宣言で、名前付き common block を索引します。
