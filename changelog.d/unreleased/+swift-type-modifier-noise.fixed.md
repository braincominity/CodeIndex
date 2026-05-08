---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift opaque and existential type modifiers no longer pollute type references** — `some`, `any`, and `each` are now ignored as modifier keywords while the real referenced types remain indexed.

## 日本語

- **Swift の opaque / existential 型修飾子が型参照を汚さなくなりました** — `some`、`any`、`each` は修飾子キーワードとして無視し、実際に参照される型だけを index するようにしました。
