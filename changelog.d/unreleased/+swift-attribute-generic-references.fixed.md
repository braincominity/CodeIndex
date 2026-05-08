---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SwiftReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift attribute generic arguments are now indexed as type references** — property wrappers and macros such as `@Relationship<UserViewModel>` now expose their generic model types without treating the attribute name itself as a type.

## 日本語

- **Swift 属性の generic 引数を型参照として index するようにしました** — `@Relationship<UserViewModel>` のような property wrapper や macro から generic のモデル型を拾い、属性名そのものは型として扱いません。
