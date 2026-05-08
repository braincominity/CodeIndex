---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift `#keyPath` roots are now indexed as type references** — `#keyPath(Person.name)` and nested paths such as `#keyPath(Person.address.street)` expose `Person` while leaving key-path member names out of type-reference results.

## 日本語

- **Swift の `#keyPath` root を型参照として index するようにしました** — `#keyPath(Person.name)` や `#keyPath(Person.address.street)` から `Person` を拾い、key-path member 名は型参照にしないようにしました。
