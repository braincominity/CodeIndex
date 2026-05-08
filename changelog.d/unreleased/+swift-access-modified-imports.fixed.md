---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift access-modified imports are now indexed** — declarations such as `public import Logging` and `package import struct PackageKit.Token` now produce searchable import symbols with the same granular-import normalization as ordinary imports.

## 日本語

- **Swift の access modifier 付き import を index するようにしました** — `public import Logging` や `package import struct PackageKit.Token` を通常の import と同じ granular import 正規化で検索可能な import シンボルにします。
