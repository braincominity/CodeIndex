---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `__all__.extend` handles next-line values** — `__all__.extend(` followed by a literal list or tuple on the next line now indexes those exported names for exact symbol search.

## 日本語

- **Python の `__all__.extend` が次行開始の値にも対応しました** — `__all__.extend(` の次行に literal list や tuple を置く形式でも、export 名を exact symbol search 用に index するようになりました。
