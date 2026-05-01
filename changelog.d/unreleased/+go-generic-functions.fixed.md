---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Go generic functions are now indexed** — `func Identity[T any](value T) T { ... }` now emits a `function` symbol instead of being skipped, so search and navigation find generic Go functions alongside ordinary ones.

## 日本語

- **Go のジェネリック関数を index するようになりました** — `func Identity[T any](value T) T { ... }` が `function` シンボルとして出力されるようになり、通常の Go 関数と同様に検索やナビゲーションで見つかるようになります。
