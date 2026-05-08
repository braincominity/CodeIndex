---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SwiftSymbolNameNormalizer.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift granular imports now use searchable symbol names** — `import struct Foundation.URL`, `import enum Dispatch.DispatchQoS`, and `import func Darwin.C.printf` drop the import kind prefix from the indexed symbol name.

## 日本語

- **Swift の granular import を検索しやすいシンボル名に正規化しました** — `import struct Foundation.URL` / `import enum Dispatch.DispatchQoS` / `import func Darwin.C.printf` から import 種別 prefix を取り除いて index します。
