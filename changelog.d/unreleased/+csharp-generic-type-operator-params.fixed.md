---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C# generic type operators no longer emit type-parameter self references** - single-line generic methods such as `value is T` and `value as T` now suppress the generic parameter while preserving real `is`/`as` target types.

## 日本語

- **C# の generic type operator が型パラメータ自身を型参照として出さないようになりました** - `value is T` / `value as T` のような単一行 generic method では generic parameter を抑制し、実際の `is` / `as` 対象型は保持します。
