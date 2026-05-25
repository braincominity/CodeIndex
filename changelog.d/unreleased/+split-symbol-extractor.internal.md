---
category: internal
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Dockerfile.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
---

## English

- **Split SymbolExtractor internals without behavior changes** — moved extraction input preparation, early exits, normalization, plugin dispatch, and Dockerfile symbol helpers out of the oversized symbol extractor.

## 日本語

- **SymbolExtractor の内部構造を挙動変更なしで分割しました** — 巨大な symbol extractor から入力準備、早期 return、正規化、plugin dispatch、Dockerfile symbol helper を切り出しました。
