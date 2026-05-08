---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript string-literal export names are now searchable without quotes** — local and re-export lists such as `export { handler as "x-api" }` and `export { remote as "remote-key" } from "./remote"` now add exported `property` symbols named `x-api` / `remote-key`.

## 日本語

- **JavaScript/TypeScript の string-literal export 名を引用符なしで検索できるようになりました** — `export { handler as "x-api" }` や `export { remote as "remote-key" } from "./remote"` のような local / re-export list が、`x-api` / `remote-key` という exported `property` シンボルを追加します。
