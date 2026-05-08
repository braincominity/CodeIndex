---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript dynamic imports now index no-substitution template literals** — `` import(`./view.js`) `` now adds an `import` symbol for `./view.js`, while interpolated templates such as `` import(`./${name}.js`) `` stay skipped.

## 日本語

- **JavaScript/TypeScript の dynamic import で no-substitution template literal を索引するようになりました** — `` import(`./view.js`) `` が `./view.js` の `import` シンボルを追加し、`` import(`./${name}.js`) `` のような interpolation 付き template は引き続きスキップされます。
