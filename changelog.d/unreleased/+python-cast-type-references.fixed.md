---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `cast` calls emit target type references** — `cast(models.User, value)` now records `User` as a `type_reference` so type-narrowing casts are searchable from the target type.

## 日本語

- **Python の `cast` 呼び出しが対象型参照を出すようになりました** — `cast(models.User, value)` が `User` を `type_reference` として記録し、型ナローイング用の cast を対象型から検索できるようになりました。
