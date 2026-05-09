---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Exported C++ namespaces are indexed** — `export namespace api {}` and qualified exported namespaces now appear in symbol and definition searches with nested declarations scoped correctly.

## 日本語

- **export付き C++ namespace を index するようになりました** — `export namespace api {}` や修飾付きexport namespaceが symbol / definition search に出て、内部宣言にも正しくscopeが付きます。
