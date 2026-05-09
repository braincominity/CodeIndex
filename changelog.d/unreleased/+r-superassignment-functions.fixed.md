---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R superassignment function definitions are now indexed** — `cdidx` recognizes `name <<- function(...)` and backtick-escaped variants as function symbols, so symbol search no longer misses functions assigned into enclosing environments.

## 日本語

- **R の superassignment による関数定義も index されるようになりました** — `cdidx` は `name <<- function(...)` と backtick 付きの同種構文を関数シンボルとして認識するため、外側の環境へ代入された関数が symbol search から抜けなくなります。
