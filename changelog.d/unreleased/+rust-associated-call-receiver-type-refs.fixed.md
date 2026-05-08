---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust associated-call receivers now emit type references** — calls such as `User::new()` and `Vec::<User>::new()` now keep the receiver type visible to reference search.

## 日本語

- **Rust の associated call receiver が type reference を出すようになりました** — `User::new()` や `Vec::<User>::new()` の receiver 型を参照検索で見つけられます。
