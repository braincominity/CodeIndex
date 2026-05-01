---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **HTML now treats common resource-bearing tags as searchable imports** — `SymbolExtractor` now emits `import` symbols for `img[src]`, `iframe[src]`, media `src`/`poster` values, `object[data]`, and SVG `use[href]` / `image[href]` in addition to the existing `script[src]` and `link[href]` coverage, so `definition` / `references` can jump to more real asset targets in HTML-heavy projects.

## 日本語

- **HTML でよく使う resource-bearing タグも検索可能な import として扱うようになりました** — `SymbolExtractor` は従来の `script[src]` / `link[href]` に加えて、`img[src]`、`iframe[src]`、media 系の `src` / `poster`、`object[data]`、SVG の `use[href]` / `image[href]` からも `import` シンボルを出すようになり、HTML が多いプロジェクトでも `definition` / `references` から実資産の参照先へ飛びやすくなりました。
