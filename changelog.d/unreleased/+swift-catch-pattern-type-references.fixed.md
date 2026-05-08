---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift catch pattern roots are now indexed as type references** — `catch NetworkError.timeout` and similar clauses expose the error type without indexing the enum case name as a type.

## 日本語

- **Swift の catch パターン root を型参照として index するようにしました** — `catch NetworkError.timeout` などでエラー型を拾い、enum case 名は型として扱わないようにしました。
