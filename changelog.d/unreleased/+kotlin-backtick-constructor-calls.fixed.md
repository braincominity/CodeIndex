---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin backticked constructor calls now emit canonical instantiation references** - `` `Display Name`() `` now records an `instantiate` edge to `Display Name`, matching the class declaration symbol.

## 日本語

- **Kotlin の backtick 付き constructor call を canonical な instantiate 参照として記録するようになりました** - `` `Display Name`() `` で、class 宣言の symbol 名と同じ `Display Name` への `instantiate` edge を記録します。
