---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.TypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin type projection modifiers are no longer indexed as type references** - type positions such as `Producer<out Payload>` and `Consumer<in Payload>` now keep `Payload` as the dependency without adding `out` or `in` noise.

## 日本語

- **Kotlin の type projection modifier が型参照として混ざらないようになりました** - `Producer<out Payload>` や `Consumer<in Payload>` の型位置で、`Payload` だけを依存として残し、`out` / `in` のノイズを追加しません。
