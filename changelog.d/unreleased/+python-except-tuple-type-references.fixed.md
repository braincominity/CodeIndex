---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python tuple `except` clauses emit type references** — handlers such as `except (TimeoutError, network.NetworkError):` now record each exception type for reference search.

## 日本語

- **Python の tuple 形式 `except` 節が型参照を出すようになりました** — `except (TimeoutError, network.NetworkError):` のような複数例外ハンドラが各例外型を参照検索に記録するようになりました。
