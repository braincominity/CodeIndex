---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran finalizer declarations without `::` now appear in reference search** — `final cleanup` is indexed the same way as `final :: cleanup`.

## 日本語

- **`::` を省略した Fortran finalizer 宣言が参照検索に出るようになりました** — `final cleanup` を `final :: cleanup` と同じように索引します。
