---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **TypeScript auto-accessor fields with initializers are now indexed as properties** — `accessor foo: T = ...;` and `accessor foo = ...;` now emit `property` symbols and keep scanning through the initializer so later class members are still indexed correctly.

## 日本語

- **initializer 付きの TypeScript auto-accessor を property として index するようになりました** — `accessor foo: T = ...;` と `accessor foo = ...;` が `property` シンボルとして出力され、initializer をまたいでスキャンを継続するため後続のクラスメンバも正しく index されます。
