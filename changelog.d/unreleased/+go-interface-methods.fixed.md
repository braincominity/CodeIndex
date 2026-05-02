---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Go interface methods are now indexed as functions** — `search`, `definition`, and other symbol-driven flows now surface method names declared inside Go interface bodies, including grouped `type (...)` blocks and single-line interface bodies.

## 日本語

- **Go の interface メソッドが function としてインデックスされるようになりました** — Go の interface 本体内で宣言されたメソッド名が `search` / `definition` などのシンボル駆動フローに現れるようになり、`type (...)` の grouped block や 1 行 interface body も対象になります。
