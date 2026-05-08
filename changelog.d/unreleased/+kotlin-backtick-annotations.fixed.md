---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin backticked annotations now emit canonical metadata references** - usages such as ``@`Fancy Name` `` and ``@`Fancy Name`("x")`` now record annotation references to `Fancy Name`.

## 日本語

- **Kotlin の backtick 付き annotation を canonical な metadata 参照として記録するようになりました** - ``@`Fancy Name` `` や ``@`Fancy Name`("x")`` で、`Fancy Name` への annotation 参照を記録します。
