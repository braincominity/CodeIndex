---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ symbol search now keeps `decltype(auto)` functions visible** — the C++ function extractor now accepts `decltype(auto)` return-type atoms, so modern functions such as `constexpr decltype(auto) value()` are indexed and searchable instead of falling through the generic function heuristic.

## 日本語

- **C++ の symbol 検索で `decltype(auto)` 関数が見えるようになりました** — C++ の function extractor が `decltype(auto)` を戻り値型トークンとして受け入れるようになり、`constexpr decltype(auto) value()` のような現代的な関数が汎用ヒューリスティックに落ちずに index / search されるようになりました。
