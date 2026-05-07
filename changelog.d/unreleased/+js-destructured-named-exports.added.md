---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
  - DEVELOPER_GUIDE.md
---

## English

- **JavaScript and TypeScript destructured named exports are now indexed** — `export const { foo, renamed: localName } = source` now emits export-surface `property` symbols for the actual binding names, including rest bindings and nested object/array binding names.

## 日本語

- **JavaScript / TypeScript の destructured named export を索引するようになりました** — `export const { foo, renamed: localName } = source` が、rest binding やネストした object / array binding 名を含め、実際の binding 名を export surface の `property` シンボルとして出すようになりました。
