---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C typedef aliases for tagged structs and enums are now indexed** — `SymbolExtractor` now records aliases such as `typedef struct Node Node_t;` and `typedef enum Mode Mode_t;`, so common C type names are searchable in `symbols` and definition-oriented views instead of being skipped.

## 日本語

- **C の tagged struct / enum の typedef alias を索引するようになりました** — `SymbolExtractor` が `typedef struct Node Node_t;` や `typedef enum Mode Mode_t;` のような alias を記録するため、よく使われる C の型名が `symbols` や definition 系の表示から落ちずに検索できるようになりました。
