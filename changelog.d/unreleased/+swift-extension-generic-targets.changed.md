---
category: changed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift extension targets now keep generic specializations searchable** — `symbols` and `search` now index declarations like `extension Array<String> where ...` under the concrete extension target instead of dropping them back to the unspecialized base type only.

## 日本語

- **Swift の extension 対象で generic specialization も検索可能にしました** — `symbols` / `search` が `extension Array<String> where ...` のような宣言を、非 specialized な base type のみではなく具体的な extension 対象として索引化するようになりました。
