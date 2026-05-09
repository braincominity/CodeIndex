---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `create_model` assignments are indexed as classes** — dynamic model declarations such as `RuntimeUser = create_model(...)` now appear in symbol search.

## 日本語

- **Python の `create_model` 代入を class として index するようにしました** — `RuntimeUser = create_model(...)` のような動的 model 宣言が symbol search に出るようになりました。
