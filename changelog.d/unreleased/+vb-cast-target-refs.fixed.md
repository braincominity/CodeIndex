---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB cast target types are now indexed as references** — `DirectCast`, `TryCast`, and `CType` target types now produce `type_reference` rows so `references` and `impact` can find cast-only Visual Basic dependencies.

## 日本語

- **VB の cast 先型を reference として索引するようにしました** — `DirectCast`、`TryCast`、`CType` の変換先型が `type_reference` になり、cast でしか現れない Visual Basic の依存関係も `references` / `impact` で見つけられます。
