---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin generic class literals no longer emit type-parameter self references** - `T::class` inside single-line generic functions now suppresses the generic parameter while real class literals such as `User::class` remain searchable.

## 日本語

- **Kotlin の generic class literal が型パラメータ自身を型参照として出さないようになりました** - 単一行 generic function 内の `T::class` では generic parameter を抑制し、`User::class` のような実際の class literal は検索可能なままにします。
