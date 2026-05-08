---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.TypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin backticked type references now use canonical names** - type positions such as ``val value: `Display Name` `` now emit `Display Name` as a `type_reference`, matching the canonical symbol name used for the declaration.

## 日本語

- **Kotlin の backtick 付き型参照を canonical 名で発行するようになりました** - ``val value: `Display Name` `` のような型位置で、宣言側と同じ canonical symbol 名 `Display Name` を `type_reference` として記録します。
