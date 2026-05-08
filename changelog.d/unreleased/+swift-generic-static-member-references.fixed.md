---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift generic static member expressions now index root and argument types** — `Result<User, Failure>.success(...)` exposes `Result`, `User`, and `Failure` without treating member `success` as a type.

## 日本語

- **Swift の generic static member 式で root とgeneric引数を index するようにしました** — `Result<User, Failure>.success(...)` から `Result`、`User`、`Failure` を辿れる一方、member 名の `success` は型として扱いません。
