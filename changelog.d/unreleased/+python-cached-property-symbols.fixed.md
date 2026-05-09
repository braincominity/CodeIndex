---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python cached properties are indexed as properties** — `@cached_property` and qualified cached property decorators now classify the decorated `def` as a property symbol instead of a function.

## 日本語

- **Python の cached property を property として index するようにしました** — `@cached_property` と修飾付き cached property decorator が付いた `def` を function ではなく property symbol として分類するようにしました。
