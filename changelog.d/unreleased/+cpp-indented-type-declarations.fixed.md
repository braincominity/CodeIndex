---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
---

## English

- **Indented C++ type declarations remain indexed** — namespace-scoped `class`, `struct`, and `union` declarations keep working after the exported/template declaration matcher improvements.

## 日本語

- **インデントされた C++ type declaration の index を維持しました** — export/template 宣言対応後も、namespace 内の `class`、`struct`、`union` 宣言を引き続き拾います。
