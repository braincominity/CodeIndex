---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **HTML resource-link coverage now includes `srcset` and navigable anchor/image-map links** — `SymbolExtractor` now emits `import` symbols for `img[srcset]` and `source[srcset]` by extracting each URL candidate, and it also treats `a[href]` and `area[href]` as searchable imports. This extends the earlier HTML resource-link follow-up so `definition` / `references` can reach more asset and navigation targets from HTML-heavy codebases.

## 日本語

- **HTML の resource-link 対応を `srcset` と遷移可能な anchor / image-map link まで広げました** — `SymbolExtractor` は `img[srcset]` と `source[srcset]` から各 URL candidate を `import` として出力し、`a[href]` と `area[href]` も検索可能な import として扱います。前回の HTML resource-link follow-up を拡張し、HTML が多いコードベースでも `definition` / `references` からより多くの資産・遷移先へ辿れるようにしました。
