---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby Rails `create_table` declarations now index table names** — `cdidx` records symbol and string table names passed to `create_table` while suppressing migration option keys.

## 日本語

- **Ruby Rails の `create_table` 宣言がtable名を索引するようになりました** — `cdidx` は `create_table` に渡されたsymbol/stringのtable名を記録し、migration option keyは参照から除外します。
