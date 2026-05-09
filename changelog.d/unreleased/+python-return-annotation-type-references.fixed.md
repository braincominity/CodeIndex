---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python return annotations emit type references** — `def load() -> models.User:` now records the return type for reference search.

## 日本語

- **Python の戻り値型注釈が型参照を出すようになりました** — `def load() -> models.User:` が戻り値型を参照検索に記録するようになりました。
