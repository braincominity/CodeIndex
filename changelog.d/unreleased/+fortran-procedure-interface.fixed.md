---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran `procedure(Interface)` declarations now record interface references** — procedure pointer and dummy procedure declarations now index the interface name as a type reference for dependency search.

## 日本語

- **Fortran の `procedure(Interface)` 宣言が interface 参照を記録するようになりました** — procedure pointer や dummy procedure の宣言で interface 名を型参照として索引し、依存検索で見つけられるようにしました。
