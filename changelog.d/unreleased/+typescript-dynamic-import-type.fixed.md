---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **TypeScript `typeof import("./mod")` now maps to an indexed import target** - dynamic `import("...")` module specifiers are indexed as `import` symbols, and bare `typeof import("./mod")` type queries now resolve to that concrete module target instead of disappearing as an unmapped type query.

## 日本語

- **TypeScript の `typeof import("./mod")` がインデックス済みの import ターゲットに紐づくようになりました** - dynamic `import("...")` の module specifier を `import` シンボルとして index し、bare な `typeof import("./mod")` 型クエリも、未解決のまま消えず concrete な module target に解決するようにしました。
