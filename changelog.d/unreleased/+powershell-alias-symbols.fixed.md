---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PowerShell alias definitions now surface in symbol search** — `Set-Alias` and `New-Alias` declarations are indexed as `alias`, so common shorthand entry points like `gci` and `ls` are easier to discover in PowerShell projects.

## 日本語

- **PowerShell の alias 定義がシンボル検索に現れるようになりました** — `Set-Alias` と `New-Alias` の宣言が `alias` として索引されるため、`gci` や `ls` のような一般的な短縮エントリポイントを PowerShell プロジェクトで見つけやすくなります。
