---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python qualified `typing.cast` calls emit target type references** — `typing.cast(models.User, value)` now records the cast target type for reference search.

## 日本語

- **Python の qualified `typing.cast` 呼び出しが対象型参照を出すようになりました** — `typing.cast(models.User, value)` が cast 対象の型を参照検索に記録するようになりました。
