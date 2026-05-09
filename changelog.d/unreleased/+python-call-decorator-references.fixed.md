---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python call-style decorators emit decorator references** — decorators such as `@parametrized(...)` now produce a `decorator` reference in addition to the ordinary call edge.

## 日本語

- **Python の呼び出し形 decorator が decorator reference を出すようになりました** — `@parametrized(...)` のような decorator が通常の call edge に加えて `decorator` reference も生成するようになりました。
