---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---
## English
- Fixed Visual Basic XML namespace imports so prefixes such as `Imports <xmlns:ui="...">` are searchable as `ui` import symbols.

## 日本語
- Visual Basic の XML 名前空間 import で、`Imports <xmlns:ui="...">` のような prefix を `ui` import シンボルとして検索できるよう修正しました。
