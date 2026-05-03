---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **CSS descendant selectors now keep nested class references visible** — `ReferenceExtractor` no longer skips selector lists that start with an element selector, so patterns like `button .card` continue to index `.card` as a searchable reference.

## 日本語

- **CSS の descendant selector でも入れ子の class reference が見えるようになりました** — `ReferenceExtractor` は要素セレクタで始まる selector list もスキップしなくなり、`button .card` のようなパターンでも `.card` が検索可能な reference として index されます。
