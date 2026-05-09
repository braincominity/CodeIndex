---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `NewType` calls emit underlying type references** — `NewType("UserId", models.User)` now records the wrapped type for reference search.

## 日本語

- **Python の `NewType` 呼び出しが元型参照を出すようになりました** — `NewType("UserId", models.User)` がラップ元の型を参照検索に記録するようになりました。
