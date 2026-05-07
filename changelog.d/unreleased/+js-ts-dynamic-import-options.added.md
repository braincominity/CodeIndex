---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript dynamic import search now handles import options** — runtime calls such as `import("./data.json", { with: { type: "json" } })` and multiline assertion-style options now surface the module specifier as an `import` symbol.

## 日本語

- **JavaScript/TypeScript の dynamic import 検索が import options に対応しました** — `import("./data.json", { with: { type: "json" } })` や複数行の assertion 形式 options を含む runtime 呼び出しでも、module specifier が `import` シンボルとして表面化します。
