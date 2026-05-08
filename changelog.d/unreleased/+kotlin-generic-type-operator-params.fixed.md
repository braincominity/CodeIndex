---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/TypedLanguageReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin generic type operators no longer emit type-parameter self references** - `value is T` and `value as T` inside generic functions now suppress the generic parameter while keeping real cast/test target types searchable.

## 日本語

- **Kotlin の generic type operator が型パラメータ自身を型参照として出さないようになりました** - generic function 内の `value is T` / `value as T` では generic parameter を抑制し、実際の cast/test 対象型は検索可能なままにします。
