---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python `pytest.raises` calls emit exception type references** — `pytest.raises(errors.ValidationError)` now records the expected exception type for reference search.

## 日本語

- **Python の `pytest.raises` 呼び出しが例外型参照を出すようになりました** — `pytest.raises(errors.ValidationError)` が期待される例外型を参照検索に記録するようになりました。
