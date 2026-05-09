---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python parameter annotations emit type references** — `def save(user: models.User):` now records the parameter type for reference search.

## 日本語

- **Python の引数型注釈が型参照を出すようになりました** — `def save(user: models.User):` が引数型を参照検索に記録するようになりました。
