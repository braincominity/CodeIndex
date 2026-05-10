---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `deallocate` object lists now appear in reference search** — targets in `deallocate(a, b, stat=...)` are indexed while keyword arguments are ignored.

## 日本語

- **Fortran の `deallocate` object list が参照検索に出るようになりました** — `deallocate(a, b, stat=...)` の対象を索引し、keyword 引数は除外します。
