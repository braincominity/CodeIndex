---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python relative import modules gain qualified symbols** — relative `from .tools import build` statements now index the resolved module name, such as `package.subpkg.tools`, in addition to imported members.

## 日本語

- **Python の relative import module に修飾済み symbol を追加しました** — `from .tools import build` のような relative import が、import された member に加えて `package.subpkg.tools` のような解決後の module 名も index するようになりました。
