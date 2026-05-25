---
category: internal
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.State.cs
---

## English

- **Split ReferenceExtractor internals without behavior changes** — moved shared extraction state into a dedicated partial file and extracted PHP line preamble plus core setup helper construction from the oversized core loop.

## 日本語

- **ReferenceExtractor の内部構造を挙動変更なしで分割しました** — 共有抽出状態を専用 partial ファイルへ移し、巨大な core ループから PHP 行前処理の参照 emit と core setup helper 構築を抽出しました。
