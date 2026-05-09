---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `TypeAlias` declarations are indexed** — legacy aliases such as `JsonValue: TypeAlias = ...` and `Handler: typing.TypeAlias = ...` now appear in symbol search.

## 日本語

- **Python の `TypeAlias` 宣言を index するようにしました** — `JsonValue: TypeAlias = ...` や `Handler: typing.TypeAlias = ...` のような legacy alias が symbol search に出るようになりました。
