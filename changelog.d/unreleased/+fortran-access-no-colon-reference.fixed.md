---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran access statements without `::` now appear in reference search** — `public name` and `private name` are indexed the same way as `public :: name` and `private :: name`.

## 日本語

- **`::` を省略した Fortran access 文が参照検索に出るようになりました** — `public name` と `private name` を `public :: name` / `private :: name` と同じように索引します。
