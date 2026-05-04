---
category: internal
affected:
  - src/CodeIndex/Indexer/Symbols/CSharpSymbolNameNormalizer.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - DEVELOPER_GUIDE.md
---

## English

- **C# symbol-name normalization now lives outside the large symbol extractor** — verbatim identifier, indexer, and conversion-operator canonicalization moved into `CSharpSymbolNameNormalizer`, reducing `SymbolExtractor` size while preserving existing extracted symbol names.

## 日本語

- **C# シンボル名正規化を巨大な symbol extractor から分離しました** — verbatim 識別子、indexer、conversion operator の canonical 化を `CSharpSymbolNameNormalizer` に移し、既存の抽出シンボル名を保ったまま `SymbolExtractor` のサイズを削減しました。
