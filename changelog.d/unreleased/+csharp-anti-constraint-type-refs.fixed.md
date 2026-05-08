---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C# anti-constraint keywords no longer become type references** - `where T : allows ref struct` now avoids `allows` / `ref` keyword noise while keeping real constraint types.

## 日本語

- **C# の anti-constraint keyword が型参照として混ざらないようになりました** - `where T : allows ref struct` で `allows` / `ref` の keyword ノイズを避けつつ、実際の制約型は維持します。
