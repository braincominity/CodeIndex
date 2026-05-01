---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift extension target parsing now handles nested generics and conformances** — `symbols` and `search` now keep `extension Foo<Bar<Baz>>: Protocol where ...` indexed under the concrete extension target instead of relying on a brittle line regex.

## 日本語

- **Swift の extension target 解析がネストした generic と conformance を扱えるようになりました** — `symbols` / `search` が `extension Foo<Bar<Baz>>: Protocol where ...` のような宣言を、壊れやすい行単位 regex ではなく具体的な extension target として索引化します。
