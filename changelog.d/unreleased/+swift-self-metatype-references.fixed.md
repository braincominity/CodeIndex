---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift `.self` metatype expressions now index their root types** — `User.self`, `Service.self`, and `[User].self` surface their concrete types while instance `user.self` remains ignored.

## 日本語

- **Swift の `.self` metatype 式で root 型を index するようにしました** — `User.self` / `Service.self` / `[User].self` の具体型を拾い、instance の `user.self` は型参照として扱わないようにしました。
