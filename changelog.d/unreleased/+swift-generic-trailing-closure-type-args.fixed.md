---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift generic trailing-closure calls now index type arguments** — expressions like `Task<User, Failure> { ... }` now expose `User` and `Failure` just like parenthesized generic calls.

## 日本語

- **Swift generic trailing-closure 呼び出しの型引数を index するようにしました** — `Task<User, Failure> { ... }` のような式で、括弧付きgeneric呼び出しと同様に `User` / `Failure` を拾います。
