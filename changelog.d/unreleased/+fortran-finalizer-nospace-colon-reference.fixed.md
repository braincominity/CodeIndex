---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran finalizer declarations with compact `::` now appear in reference search** — `final::cleanup` is indexed the same way as spaced finalizer declarations.

## 日本語

- **空白なし `::` の Fortran finalizer 宣言が参照検索に出るようになりました** — `final::cleanup` を空白ありの finalizer 宣言と同じように索引します。
