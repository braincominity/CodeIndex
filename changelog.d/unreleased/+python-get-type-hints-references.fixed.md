---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `get_type_hints` calls emit target type references** — `get_type_hints(models.User)` now records the inspected target for reference search.

## 日本語

- **Python の `get_type_hints` 呼び出しが対象型参照を出すようになりました** — `get_type_hints(models.User)` が検査対象を参照検索に記録するようになりました。
