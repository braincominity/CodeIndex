---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust fully-qualified associated calls now emit receiver and trait type references** — `<User as Service>::handle()` indexes both `User` and `Service`.

## 日本語

- **Rust fully-qualified associated call が receiver と trait の type reference を出すようになりました** — `<User as Service>::handle()` で `User` と `Service` の両方を索引します。
