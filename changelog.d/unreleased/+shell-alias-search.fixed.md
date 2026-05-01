---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Shell aliases are now searchable as first-class symbols** — `shell` indexing now records `alias` definitions and includes them in shell callable-name matching, so `search`, `references`, and related queries can find both alias definitions and alias call sites in Linux shell scripts.

## 日本語

- **Shell alias を第一級のシンボルとして検索できるようになりました** — `shell` のインデックスで `alias` 定義を記録し、shell の callable-name 判定にも含めるようにしたため、Linux shell スクリプト内の alias 定義と alias 呼び出しの両方を `search` / `references` などで見つけられます。
