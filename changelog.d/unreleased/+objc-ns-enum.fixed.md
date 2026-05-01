---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Objective-C now indexes Apple enum macro typedefs** — `SymbolExtractor` now records `NS_ENUM`, `NS_OPTIONS`, `NS_EXTENSIBLE_ENUM`, and `NS_ERROR_ENUM` declarations as `enum` symbols, so common Objective-C type definitions show up in `symbols`, `definition`, and other search-driven views instead of being skipped.

## 日本語

- **Objective-C で Apple の enum マクロ typedef を索引するようになりました** — `SymbolExtractor` が `NS_ENUM`、`NS_OPTIONS`、`NS_EXTENSIBLE_ENUM`、`NS_ERROR_ENUM` を `enum` シンボルとして記録するため、Objective-C のよくある型定義が `symbols`、`definition`、その他の検索ベースの表示から落ちずに見えるようになりました。
