---
category: internal
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
---

## English

- **Split SymbolExtractor internals without behavior changes** — moved extraction input preparation, early exits, normalization, and plugin dispatch out of the oversized symbol extraction method.

## 日本語

- **SymbolExtractor の内部構造を挙動変更なしで分割しました** — 巨大な symbol extraction メソッドから入力準備、早期 return、正規化、plugin dispatch を切り出しました。
