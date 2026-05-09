---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ linkage-specified functions are searchable** — functions declared like `extern "C" int api() {}` now index the actual function name and return type.

## 日本語

- **linkage specification 付き C++ 関数を検索できるようになりました** — `extern "C" int api() {}` のような宣言で、実際の関数名と戻り値型を index します。
