---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C# generic constraint keywords no longer become type references** - `where T : unmanaged` and `where T : notnull` now avoid keyword noise while preserving real constraint types such as `IContract`.

## 日本語

- **C# の generic constraint keyword が型参照として混ざらないようになりました** - `where T : unmanaged` や `where T : notnull` では keyword ノイズを避けつつ、`IContract` のような実際の制約型は維持します。
