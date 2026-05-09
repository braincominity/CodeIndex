---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python generic parameter annotations emit nested type references** — `def save(users: Sequence[models.User]):` now records `User` from inside the parameter annotation.

## 日本語

- **Python の generic 引数型注釈が内側の型参照を出すようになりました** — `def save(users: Sequence[models.User]):` が引数注釈内の `User` を記録するようになりました。
