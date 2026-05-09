---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python qualified `typing.get_type_hints` calls emit target type references** — `typing.get_type_hints(models.User)` now records the inspected target for reference search.

## 日本語

- **Python の qualified `typing.get_type_hints` 呼び出しが対象型参照を出すようになりました** — `typing.get_type_hints(models.User)` が検査対象を参照検索に記録するようになりました。
