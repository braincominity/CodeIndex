---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ symbol search now keeps `decltype(expr)` functions visible** — the C++ function extractor now accepts general `decltype(...)` return-type atoms, so modern functions such as `constexpr decltype(foo(42)) value()` are indexed and searchable instead of falling through the generic function heuristic. Added a dedicated C++ class-body regression fixture for constructor, destructor, and operator-overload coverage.

## 日本語

- **C++ の symbol 検索で `decltype(expr)` 関数が見えるようになりました** — C++ の function extractor が一般的な `decltype(...)` を戻り値型トークンとして受け入れるようになり、`constexpr decltype(foo(42)) value()` のような現代的な関数が汎用ヒューリスティックに落ちずに index / search されるようになりました。あわせて、constructor / destructor / operator overload を追跡する C++ class-body 用の回帰 fixture を追加しました。
