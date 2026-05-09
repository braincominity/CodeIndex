---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **VB member fields are now searchable as property symbols** — visible fields such as `Private ReadOnly repo As Repository` and `Public Shared Count As Integer` now appear in symbol search without indexing local `Dim` variables.

## 日本語

- **VB のメンバー field を property シンボルとして検索できるようにしました** — `Private ReadOnly repo As Repository` や `Public Shared Count As Integer` のような field が symbol search に表示され、local `Dim` 変数は索引しません。
