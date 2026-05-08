---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby alias declarations now index their endpoints** — `cdidx` records the names in `alias` and `alias_method` declarations so searches can connect alias definitions to the methods they expose.

## 日本語

- **Ruby の alias 宣言が両端の名前を索引するようになりました** — `cdidx` は `alias` / `alias_method` 宣言内の名前を記録するため、alias 定義と公開されるメソッドを検索でつなげやすくなります。
