---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python qualified call decorators keep their full decorator reference** — dotted decorators such as `@pytest.mark.parametrize(...)` now emit `pytest.mark.parametrize` as a `decorator` reference.

## 日本語

- **Python の修飾付き呼び出し形 decorator が完全名の decorator reference を保持するようになりました** — `@pytest.mark.parametrize(...)` のような dotted decorator が `pytest.mark.parametrize` を `decorator` reference として出すようになりました。
