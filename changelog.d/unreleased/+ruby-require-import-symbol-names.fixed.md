---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby `require` import symbols now omit surrounding quotes** — `cdidx` indexes `require "path"` and `require_relative 'path'` import names as `path`, improving symbol search matches.

## 日本語

- **Ruby の `require` import シンボルが周囲の引用符を除いて索引されるようになりました** — `cdidx` は `require "path"` / `require_relative 'path'` の import 名を `path` として記録し、シンボル検索で一致しやすくします。
