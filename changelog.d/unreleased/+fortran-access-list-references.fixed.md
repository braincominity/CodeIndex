---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran access lists now appear in reference search** — explicit `public :: name` and `private :: name` declarations index the listed module symbols.

## 日本語

- **Fortran の access list が参照検索に出るようになりました** — 明示的な `public :: name` / `private :: name` 宣言で、列挙された module symbol を索引します。
