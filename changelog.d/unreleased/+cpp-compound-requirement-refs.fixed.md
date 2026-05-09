---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ compound requirement constraints are indexed as type references** — `requires` clauses with `{ expr } -> std::same_as<Result>` now expose both the concept and constrained result types.

## 日本語

- **C++ の compound requirement 制約を type reference として index するようになりました** — `{ expr } -> std::same_as<Result>` を含む `requires` 句が concept と制約結果型の両方を出します。
