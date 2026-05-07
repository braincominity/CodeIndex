---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C# compile-time type keyword references now include nested generic arguments** — `typeof(List<Payload>)`, `default(Dictionary<string, Payload>)`, and similar `nameof` / `sizeof` / `default` forms now emit `type_reference` rows for user types inside generic argument lists while still filtering C# built-in aliases.

## 日本語

- **C# の compile-time type keyword 参照で nested generic 引数も検索できるようになりました** — `typeof(List<Payload>)` や `default(Dictionary<string, Payload>)`、同種の `nameof` / `sizeof` / `default` で、generic 引数内のユーザー定義型も `type_reference` として発行します。C# built-in alias は引き続き除外します。
