---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin use-site annotations no longer leak into type references** - type positions such as `@field:Fancy Payload` now keep `Fancy` as annotation metadata while recording `Payload` as the type dependency.

## 日本語

- **Kotlin の use-site annotation が型参照へ混ざらないようになりました** - `@field:Fancy Payload` のような型位置で、`Fancy` は annotation metadata として扱い、`Payload` を型依存として記録します。
