---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **TypeScript `import = require(...)` declarations are now searchable as imports** — `cdidx` now indexes `import foo = require('bar')` forms so dependency lookups can find legacy TypeScript modules by path.

## 日本語

- **TypeScript の `import = require(...)` 宣言も import として検索できるようになりました** — `cdidx` は `import foo = require('bar')` 形式を index するため、従来型の TypeScript モジュールもパス指定で見つけやすくなります。
