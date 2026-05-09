---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python tuple `isinstance` checks emit type references** — `isinstance(value, (models.User, api.Admin))` now records each checked type for reference search.

## 日本語

- **Python の tuple 形式 `isinstance` が型参照を出すようになりました** — `isinstance(value, (models.User, api.Admin))` が確認対象の各型を参照検索に記録するようになりました。
