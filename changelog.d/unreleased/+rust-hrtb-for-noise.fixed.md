---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.TypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust higher-ranked trait bounds keep their real bound types visible** — lifetime binders such as `for<'a>` are masked without erasing `Fn`/payload types or emitting `for` as a type reference.

## 日本語

- **Rust の higher-ranked trait bound で実際の bound 型が残るようになりました** — `for<'a>` のような lifetime binder で `Fn` や payload 型を消さず、`for` も type reference として出さないようにしました。
