---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `isinstance` checks emit type references** — `isinstance(value, models.User)` now records the checked type so runtime type checks are searchable from the class.

## 日本語

- **Python の `isinstance` チェックが型参照を出すようになりました** — `isinstance(value, models.User)` が確認対象の型を記録し、実行時型チェックをクラス側から検索できるようになりました。
