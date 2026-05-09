---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `TypeVar` bounds emit type references** — `TypeVar("TUser", bound=models.User)` now records the bound type for reference search.

## 日本語

- **Python の `TypeVar` bound が型参照を出すようになりました** — `TypeVar("TUser", bound=models.User)` がbound型を参照検索に記録するようになりました。
