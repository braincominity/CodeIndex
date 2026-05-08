---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift metatype suffixes no longer pollute type references** — `User.Type` and `Service.Protocol` now index the real target type without adding noisy `Type` or `Protocol` reference rows.

## 日本語

- **Swift の metatype suffix が型参照を汚さなくなりました** — `User.Type` や `Service.Protocol` で実際の対象型だけを index し、`Type` / `Protocol` というノイズ行を追加しないようにしました。
