---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python generic return annotations emit nested type references** — `def load_many() -> list[models.User]:` now records `User` from inside the return annotation.

## 日本語

- **Python の generic 戻り値型注釈が内側の型参照を出すようになりました** — `def load_many() -> list[models.User]:` が戻り値注釈内の `User` を記録するようになりました。
