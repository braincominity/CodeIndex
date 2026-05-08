---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `prepend` now links to the mixed-in module** — `cdidx` treats `prepend SomeModule` like `include` and `extend`, indexing the module argument while avoiding a noisy call edge for the keyword.

## 日本語

- **Ruby の `prepend` が mixin 対象モジュールへリンクするようになりました** — `cdidx` は `prepend SomeModule` を `include` / `extend` と同様に扱い、キーワード自体のノイズになる call edge を避けつつモジュール引数を索引します。
