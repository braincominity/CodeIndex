---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Go `type` declarations now keep a type-like symbol kind** — named Go types and aliases were previously indexed as `import` entries when they were not `struct` or `interface` declarations. They now appear as `class` symbols so searches and symbol browsing reflect the actual declaration type.

## 日本語

- **Go の `type` 宣言が type 系のシンボル種別を維持するようになりました** — `struct` / `interface` 以外の Go の名前付き型やエイリアスが、これまで `import` として索引されていました。これらを `class` シンボルとして扱うことで、検索やシンボル閲覧が実際の宣言種別に近づきます。
