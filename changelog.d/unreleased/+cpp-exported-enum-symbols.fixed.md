---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Exported C++ enum declarations are indexed** — `export enum class Mode` and `export enum Status` now appear in symbol and definition searches.

## 日本語

- **export付き C++ enum 宣言を index するようになりました** — `export enum class Mode` や `export enum Status` が symbol / definition search に出ます。
