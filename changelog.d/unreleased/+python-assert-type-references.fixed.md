---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `assert_type` calls emit expected type references** — `assert_type(value, models.User)` now records the expected type for reference search.

## 日本語

- **Python の `assert_type` 呼び出しが期待型参照を出すようになりました** — `assert_type(value, models.User)` が期待型を参照検索に記録するようになりました。
