---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/JavaReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Java generic callable signatures no longer emit type-parameter self references** - signatures such as `<T extends Comparable<T>> T pick(T input)` now keep real bound types while suppressing `T` as type-reference noise.

## 日本語

- **Java の generic callable signature が型パラメータ自身を型参照として出さないようになりました** - `<T extends Comparable<T>> T pick(T input)` のような signature では実際の制約型を残しつつ、`T` の type_reference ノイズを抑制します。
