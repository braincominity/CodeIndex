---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust `use` imports now emit references** — imported target names from simple and grouped `use` statements are recorded as references without indexing aliases as types.

## 日本語

- **Rust の `use` import が reference を出すようになりました** — simple/grouped `use` の import target 名を参照として記録し、alias は型として扱いません。
