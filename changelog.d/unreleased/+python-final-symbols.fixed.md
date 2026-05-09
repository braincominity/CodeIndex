---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `Final` declarations are indexed** — module constants such as `DEFAULT_TIMEOUT: Final[int] = 30` now appear as property symbols in symbol search.

## 日本語

- **Python の `Final` 宣言を index するようにしました** — `DEFAULT_TIMEOUT: Final[int] = 30` のような module constant が property symbol として symbol search に出るようになりました。
