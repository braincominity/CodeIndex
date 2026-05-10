---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Compact Fortran `use::module` statements now appear in reference search** — compact module imports, `only:` lists, and rename lists are indexed like spaced `use :: module` statements.

## 日本語

- **compact な Fortran `use::module` 文が参照検索に出るようになりました** — 空白なしの module import、`only:` list、rename list を、空白ありの `use :: module` と同じように索引します。
