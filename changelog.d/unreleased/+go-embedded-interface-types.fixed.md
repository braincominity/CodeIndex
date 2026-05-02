---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Go embedded interface types are now indexed inside interface bodies** — embedded constraints such as `io.Reader` and `io.Writer` are surfaced as standalone `import` symbols, so `search` can find interface constraints instead of silently skipping them.

## 日本語

- **Go の埋め込み interface 型が interface 本体内でもインデックスされるようになりました** — `io.Reader` や `io.Writer` のような埋め込み制約を standalone な `import` シンボルとして表に出すため、`search` で interface 制約を見つけられるようになり、無言で取りこぼされなくなります。
