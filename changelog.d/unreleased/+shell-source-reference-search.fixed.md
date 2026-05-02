---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Shell `source` file loads are now indexed as references** — shell scripts that load another script with `source ./env.sh` now emit `reference` edges for the imported path, which makes dependency-style searches and reference lookups more complete on Linux shell projects.

## 日本語

- **Shell の `source` によるファイル読込を参照として索引するようになりました** — `source ./env.sh` で別スクリプトを読み込む shell スクリプトは、読み込まれたパスに対して `reference` エッジを出力するため、Linux shell プロジェクトでの依存関係検索と参照検索がより完全になります。
