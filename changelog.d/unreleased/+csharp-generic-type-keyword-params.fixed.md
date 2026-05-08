---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C# generic type keywords no longer emit type-parameter self references** - single-line generic methods using `typeof(T)` or `nameof(T)` now suppress the generic parameter while keeping real type keyword targets searchable.

## 日本語

- **C# の generic type keyword が型パラメータ自身を型参照として出さないようになりました** - `typeof(T)` や `nameof(T)` を使う単一行 generic method では generic parameter を抑制し、実際の type keyword 対象型は検索可能なままにします。
