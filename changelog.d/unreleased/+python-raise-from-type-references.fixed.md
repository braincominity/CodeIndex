---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `raise ... from ...` emits exception type references** — `raise package.CustomError from exc` now records the raised exception type even when exception chaining is present.

## 日本語

- **Python の `raise ... from ...` が例外型参照を出すようになりました** — `raise package.CustomError from exc` のような例外連鎖でも、送出する例外型を記録するようになりました。
