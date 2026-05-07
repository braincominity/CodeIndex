---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript CommonJS default function exports are now searchable** — `module.exports = function () {}` and `module.exports = async () => {}` now add exported `default` `function` symbols, while `module.exports = class`, object literals, and plain values continue through their existing paths.

## 日本語

- **JavaScript/TypeScript の CommonJS default function export を検索できるようになりました** — `module.exports = function () {}` や `module.exports = async () => {}` が exported `default` `function` シンボルを追加し、`module.exports = class`、object literal、通常値は既存の経路のまま扱われます。
