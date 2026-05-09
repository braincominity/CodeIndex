---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Exported C++ classes and structs are searchable** — module interface declarations such as `export class Api {}` and `export template <typename T> struct Box {}` now produce type symbols.

## 日本語

- **export された C++ class / struct を検索できるようになりました** — `export class Api {}` や `export template <typename T> struct Box {}` のような module interface 宣言が型 symbol になります。
