---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin backticked class literals now emit canonical type references** - `` `Display Name`::class `` now records `Display Name` as the referenced type, matching the declaration symbol name.

## 日本語

- **Kotlin の backtick 付き class literal を canonical な型参照として記録するようになりました** - `` `Display Name`::class `` で、宣言側の symbol 名と同じ `Display Name` を参照型として記録します。
