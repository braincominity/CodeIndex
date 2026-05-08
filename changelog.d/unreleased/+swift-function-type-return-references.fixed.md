---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/TypedLanguageReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift function type return positions now stay searchable** — colon type positions such as `(Input) -> Output` now keep the return type in `type_reference` extraction instead of stopping at `->`.

## 日本語

- **Swift の function type の戻り値型が検索対象に残るようになりました** — `(Input) -> Output` のような colon 型位置で `->` によって抽出が途切れず、戻り値型も `type_reference` として記録されるようにしました。
