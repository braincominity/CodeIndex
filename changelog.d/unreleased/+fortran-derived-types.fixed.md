---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran derived types are now searchable as definitions** — `type :: Name` and `type, extends(Base) :: Name` declarations are indexed as type definitions, and `extends(Base)` is recorded as a type reference for definition/reference search.

## 日本語

- **Fortran の派生型定義を検索できるようになりました** — `type :: Name` と `type, extends(Base) :: Name` の宣言を型定義として索引し、`extends(Base)` も definition/reference 検索用の型参照として記録します。
