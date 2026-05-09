---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python property accessors are indexed as properties** — `@name.setter` and `@name.deleter` methods now appear as property symbols instead of function symbols.

## 日本語

- **Python の property accessor を property として index するようにしました** — `@name.setter` と `@name.deleter` の method が function ではなく property symbol として出るようになりました。
