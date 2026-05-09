---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `issubclass` checks emit type references** — `issubclass(cls, services.Plugin)` now records the checked base type for reference search.

## 日本語

- **Python の `issubclass` チェックが型参照を出すようになりました** — `issubclass(cls, services.Plugin)` が確認対象の基底型を参照検索に記録するようになりました。
