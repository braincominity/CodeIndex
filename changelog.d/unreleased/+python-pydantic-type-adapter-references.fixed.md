---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `pydantic.TypeAdapter` calls emit target type references** — `pydantic.TypeAdapter(models.User)` now records the adapted model type for reference search.

## 日本語

- **Python の `pydantic.TypeAdapter` 呼び出しが対象型参照を出すようになりました** — `pydantic.TypeAdapter(models.User)` がアダプタ対象のモデル型を参照検索に記録するようになりました。
