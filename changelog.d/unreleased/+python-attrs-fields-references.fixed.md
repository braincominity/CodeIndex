---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `attrs.fields` calls emit target type references** — `attrs.fields(models.User)` and `attr.fields(models.User)` now record the inspected attrs model type for reference search.

## 日本語

- **Python の `attrs.fields` 呼び出しが対象型参照を出すようになりました** — `attrs.fields(models.User)` と `attr.fields(models.User)` が検査対象の attrs モデル型を参照検索に記録するようになりました。
