---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python tuple `issubclass` checks emit type references** — `issubclass(cls, (services.Plugin, mixins.Audited))` now records each checked base type for reference search.

## 日本語

- **Python の tuple 形式 `issubclass` が型参照を出すようになりました** — `issubclass(cls, (services.Plugin, mixins.Audited))` が確認対象の各基底型を参照検索に記録するようになりました。
