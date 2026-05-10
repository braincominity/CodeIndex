---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran allocation status arguments now appear in reference search** — `stat=` and `errmsg=` variables on `allocate` and `deallocate` statements are indexed as references.

## 日本語

- **Fortran の allocation status 引数が参照検索に出るようになりました** — `allocate` / `deallocate` 文の `stat=` と `errmsg=` 変数を参照として索引します。
