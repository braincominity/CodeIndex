---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript and TypeScript default-export functions are now indexed** — `export default function Name() {}` now emits a `function` symbol named `Name`, and anonymous default-export functions are indexed as the module `default` function surface.

## 日本語

- **JavaScript / TypeScript の default export 関数を索引するようになりました** — `export default function Name() {}` が `Name` の `function` シンボルを出し、無名の default export 関数もモジュールの `default` 関数面として索引されます。
