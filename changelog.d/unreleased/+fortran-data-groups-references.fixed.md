---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran multi-group `data` statements now appear fully in reference search** — declarations such as `data a /1/, b /2/` index each initialized object list.

## 日本語

- **Fortran の複数 group `data` 文が参照検索にすべて出るようになりました** — `data a /1/, b /2/` のような宣言で、各初期化対象リストを索引します。
