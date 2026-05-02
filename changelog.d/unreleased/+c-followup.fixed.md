---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C search now covers older K&R-style declarations and more complex vendor attributes** — this follow-up to PR 1313 teaches `SymbolExtractor` to keep indexed names for old-style definitions such as `int legacy(a, b) int a; int b; { ... }` and for nested attribute forms such as `__attribute__((format(printf, 1, 2)))` or `__declspec(align(16))`, so annotated or legacy C functions stay searchable.

## 日本語

- **C 検索が古い K&R 形式の宣言と、より複雑な vendor 属性に対応しました** — PR 1313 の follow-up として、`SymbolExtractor` が `int legacy(a, b) int a; int b; { ... }` のような old-style 定義や、`__attribute__((format(printf, 1, 2)))` / `__declspec(align(16))` のような入れ子属性でも名前を索引するため、注釈付き・古い書き方の C 関数も引き続き検索できます。
