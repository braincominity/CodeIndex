---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby association `class_name` options now link to target classes** — `cdidx` indexes string class names from Rails-style `belongs_to`, `has_one`, and `has_many` declarations so association searches can find the actual model type.

## 日本語

- **Ruby association の `class_name` option が対象クラスへリンクするようになりました** — `cdidx` は Rails 風の `belongs_to` / `has_one` / `has_many` 宣言にある文字列クラス名を索引するため、association 検索で実際のモデル型を見つけやすくなります。
