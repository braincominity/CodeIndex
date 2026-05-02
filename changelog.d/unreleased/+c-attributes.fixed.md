---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C functions with GCC/Clang/MSVC attribute specifiers now remain searchable** — `SymbolExtractor` now skips common attribute blocks such as `__attribute__((...))`, `__declspec(...)`, and `_Noreturn` when they appear before or between the return type and function name, so annotated C functions like `__attribute__((noreturn)) void die(void)` and `static inline __attribute__((always_inline)) int add(int, int)` surface in `symbols`.

## 日本語

- **GCC/Clang/MSVC の attribute specifier 付き C 関数も検索できるようになりました** — `SymbolExtractor` が `__attribute__((...))`、`__declspec(...)`、`_Noreturn` のような attribute ブロックを、戻り値型の前や戻り値型と関数名の間でスキップするため、`__attribute__((noreturn)) void die(void)` や `static inline __attribute__((always_inline)) int add(int, int)` のような注釈付き C 関数が `symbols` に現れるようになります。
