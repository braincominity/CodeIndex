---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/JavaReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Java annotated `instanceof` patterns now keep the tested type reference** — forms such as `value instanceof final @NonNull Payload payload` now emit `Payload` as a `type_reference` while keeping `NonNull` as annotation metadata.

## 日本語

- **Java の annotation 付き `instanceof` pattern で検査対象型を拾えるようになりました** — `value instanceof final @NonNull Payload payload` のような形で、`Payload` を `type_reference` として発行し、`NonNull` は annotation metadata として扱います。
