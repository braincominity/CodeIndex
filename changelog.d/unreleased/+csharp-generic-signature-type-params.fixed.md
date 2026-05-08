---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C# generic signatures no longer emit type-parameter self references** - declarations such as `class Demo<T> : Base<T>` and `T Pick<T>(T value)` now keep real types while suppressing generic parameter noise.

## 日本語

- **C# の generic signature が型パラメータ自身を型参照として出さないようになりました** - `class Demo<T> : Base<T>` や `T Pick<T>(T value)` では実際の型を残しつつ、generic parameter のノイズを抑制します。
