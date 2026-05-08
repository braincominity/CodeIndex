---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust associated values now emit receiver type references** — non-call paths such as `User::DEFAULT` and `Result::<User, Error>::Ok` index the receiver and turbofish types without treating the value name as a type.

## 日本語

- **Rust associated value が receiver の type reference を出すようになりました** — `User::DEFAULT` や `Result::<User, Error>::Ok` で receiver と turbofish 型を索引し、値名自体は型として扱いません。
