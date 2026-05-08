---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust type aliases now index their target types** — aliases such as `type UserMap = HashMap<Key, User>` now expose the right-hand-side types as references for search, inspect, and dependency workflows.

## 日本語

- **Rust の type alias が target 型をインデックスするようになりました** — `type UserMap = HashMap<Key, User>` のような alias で、右辺の型を search / inspect / dependency ワークフローから参照できます。
