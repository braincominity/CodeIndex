---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/TypedLanguageReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin generic signatures no longer emit type-parameter self references** - signatures and bounds such as `input: T`, `T : Comparable<T>`, and `where T : Handler<T>` now keep the real referenced types while suppressing `T` as type-reference noise.

## 日本語

- **Kotlin の generic signature が型パラメータ自身を型参照として出さないようになりました** - `input: T`、`T : Comparable<T>`、`where T : Handler<T>` では実際の参照型を残しつつ、`T` の type_reference ノイズを抑制します。
