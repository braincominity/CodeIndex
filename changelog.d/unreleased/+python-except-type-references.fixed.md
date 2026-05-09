---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `except` clauses emit type references** — `except CustomError as exc:` now records `CustomError` as a `type_reference`, making exception handlers discoverable from the exception class.

## 日本語

- **Python の `except` 節が型参照を出すようになりました** — `except CustomError as exc:` が `CustomError` を `type_reference` として記録し、例外クラスから捕捉箇所を探せるようになりました。
