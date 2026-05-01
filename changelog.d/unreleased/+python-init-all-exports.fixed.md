---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Python `__all__` exports in package `__init__.py` files are now searchable** — package init modules now surface `__all__` entries as import-style symbols, so `symbols --exact-name` can find public APIs exposed through package re-exports instead of requiring the concrete implementation module path. Added regressions for `__all__` extraction and for `search --exact` staying aligned with symbol lookup on the same package fixture.

## 日本語

- **Python の package `__init__.py` にある `__all__` export を検索できるようになりました** — package の init モジュールで定義された `__all__` の要素を import 形式の symbol として出すため、`symbols --exact-name` でも re-export された public API を concrete な実装モジュール名なしで見つけられます。`__all__` 抽出の回帰と、同じ package fixture に対して `search --exact` が symbol 検索と揃って動くことの回帰を追加しました。
