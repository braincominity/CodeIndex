---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Shell source-reference indexing now handles quoted operands and dot-sourcing** — `source "./quoted env.sh"` and `. ./lib/common.sh` now emit `reference` edges for the sourced file path, making Linux shell dependency searches and file lookups more complete.

## 日本語

- **Shell の source 参照索引が quoted operand と dot-sourcing に対応しました** — `source "./quoted env.sh"` と `. ./lib/common.sh` が参照先のファイルパスに対して `reference` エッジを出力するようになり、Linux shell の依存関係検索とファイル検索がより完全になります。
