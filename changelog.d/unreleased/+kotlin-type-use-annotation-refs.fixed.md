---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin type-use annotations no longer appear as type references** — annotated type positions such as `val value: @Fancy Payload` now keep `Payload` as the `type_reference` dependency while leaving `Fancy` as metadata instead of a phantom type.

## 日本語

- **Kotlin の type-use annotation が型参照として混ざらないようになりました** — `val value: @Fancy Payload` のような型位置では、`Payload` を `type_reference` として残しつつ、`Fancy` は metadata として扱い phantom な型参照にしません。
