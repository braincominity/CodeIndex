---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript local named export lists are now searchable** — `export { foo, local as publicName }` and TypeScript `export type { User }` now add exported `property` symbols even when the declaration and export list are separated.

## 日本語

- **JavaScript/TypeScript の local named export list を検索できるようになりました** — `export { foo, local as publicName }` や TypeScript の `export type { User }` が、宣言と export list が分かれていても exported `property` シンボルとして追加されます。
