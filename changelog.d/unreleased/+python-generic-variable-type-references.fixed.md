---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python generic variable annotations emit nested type references** — `users: Sequence[models.User] = []` now records `User` from inside the variable annotation.

## 日本語

- **Python の generic 変数型注釈が内側の型参照を出すようになりました** — `users: Sequence[models.User] = []` が変数注釈内の `User` を記録するようになりました。
