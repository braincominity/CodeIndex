---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript static imports now surface source modules** — declarations such as `import React from "react"`, multiline `import { ... } from "vue"`, side-effect imports, and import-attributes forms now add the source module specifier as an `import` symbol.

## 日本語

- **JavaScript/TypeScript の静的 import が source module を表面化するようになりました** — `import React from "react"`、複数行の `import { ... } from "vue"`、side-effect import、import attributes 付き import が、source module specifier を `import` シンボルとして追加します。
