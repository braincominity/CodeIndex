---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `TypeVar` constraints emit type references** — `TypeVar("TAccount", models.User, models.Admin)` now records each constraint type for reference search.

## 日本語

- **Python の `TypeVar` 制約が型参照を出すようになりました** — `TypeVar("TAccount", models.User, models.Admin)` が各制約型を参照検索に記録するようになりました。
