---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript `require.resolve()` calls with options are now searchable** — `require.resolve("./with-paths", { paths: [...] })` now keeps the static module specifier as an `import` symbol even when the call includes a trailing options argument.

## 日本語

- **JavaScript/TypeScript の options 付き `require.resolve()` call を検索できるようになりました** — `require.resolve("./with-paths", { paths: [...] })` が trailing options 引数を含む場合でも、静的な module specifier を `import` シンボルとして保持します。
