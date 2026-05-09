---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `dataclasses.fields` calls emit target type references** — `dataclasses.fields(models.User)` now records the inspected dataclass type for reference search.

## 日本語

- **Python の `dataclasses.fields` 呼び出しが対象型参照を出すようになりました** — `dataclasses.fields(models.User)` が検査対象の dataclass 型を参照検索に記録するようになりました。
