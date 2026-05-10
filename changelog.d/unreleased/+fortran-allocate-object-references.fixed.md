---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `allocate` object lists now appear in reference search** — allocated objects in `allocate(Type :: object)` and `allocate(object, stat=...)` are indexed while keyword arguments are ignored.

## 日本語

- **Fortran の `allocate` object list が参照検索に出るようになりました** — `allocate(Type :: object)` や `allocate(object, stat=...)` の確保対象を索引し、keyword 引数は除外します。
