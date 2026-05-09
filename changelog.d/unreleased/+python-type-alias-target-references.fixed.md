---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python type aliases emit target type references** — `UserAlias: TypeAlias = models.User` now records the aliased target type for reference search.

## 日本語

- **Python の型エイリアスが対象型参照を出すようになりました** — `UserAlias: TypeAlias = models.User` がエイリアス先の型を参照検索に記録するようになりました。
