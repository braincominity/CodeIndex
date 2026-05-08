---
category: changed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Go generic function constraints are indexed as type references** — `func Decode[T WireMessage](...)` now surfaces `WireMessage` in reference search and inspect output.
- **Go generic type constraints are indexed as type references** — `type Cache[T EntityConstraint] ...` now surfaces `EntityConstraint` in reference search and inspect output.

## 日本語

- **Go の generic function 制約を型参照として索引するようになりました** — `func Decode[T WireMessage](...)` から `WireMessage` が reference search と inspect 出力に現れるようになりました。
- **Go の generic type 制約を型参照として索引するようになりました** — `type Cache[T EntityConstraint] ...` から `EntityConstraint` が reference search と inspect 出力に現れるようになりました。
