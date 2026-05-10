---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `data` object lists now appear in reference search** — declarations such as `data a, b /.../` index the initialized object names.

## 日本語

- **Fortran の `data` object list が参照検索に出るようになりました** — `data a, b /.../` のような宣言で、初期化対象の名前を索引します。
