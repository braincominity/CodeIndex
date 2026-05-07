---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript and TypeScript multiline dynamic imports are now indexed** — runtime `import(\n  "./module"\n)` calls now emit `import` symbols for their module specifiers while strings, comments, and TypeScript `typeof import(...)` type queries stay excluded.

## 日本語

- **JavaScript / TypeScript の複数行 dynamic import を索引するようになりました** — runtime の `import(\n  "./module"\n)` 呼び出しが module specifier の `import` シンボルを出すようになり、文字列、コメント、TypeScript の `typeof import(...)` type query は引き続き除外されます。
