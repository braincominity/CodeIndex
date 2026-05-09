---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python abstract properties are indexed as properties** — legacy `@abstractproperty` and qualified `@abc.abstractproperty` decorators now classify decorated methods as property symbols.

## 日本語

- **Python の abstract property を property として index するようにしました** — legacy の `@abstractproperty` と修飾付き `@abc.abstractproperty` decorator が付いた method を property symbol として分類するようにしました。
