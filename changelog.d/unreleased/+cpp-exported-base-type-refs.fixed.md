---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Exported C++ type base lists are indexed** — `export class Child : public Base` now emits type references for inherited base types.

## 日本語

- **export された C++ 型の base list を index するようになりました** — `export class Child : public Base` が継承先の型 reference を出します。
