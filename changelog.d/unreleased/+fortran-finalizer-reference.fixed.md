---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran finalizer declarations now appear in reference search** — `final :: cleanup, destroy` declarations index each finalizer procedure.

## 日本語

- **Fortran の finalizer 宣言が参照検索に出るようになりました** — `final :: cleanup, destroy` 宣言で列挙された各 finalizer 手続きを索引します。
