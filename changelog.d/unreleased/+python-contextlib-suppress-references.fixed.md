---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `contextlib.suppress` calls emit exception type references** — `contextlib.suppress(errors.NotFoundError)` now records the suppressed exception type for reference search.

## 日本語

- **Python の `contextlib.suppress` 呼び出しが例外型参照を出すようになりました** — `contextlib.suppress(errors.NotFoundError)` が握りつぶす例外型を参照検索に記録するようになりました。
