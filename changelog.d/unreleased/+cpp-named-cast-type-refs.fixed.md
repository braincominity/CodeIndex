---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ named casts expose target type references** — `static_cast<Foo*>`, `dynamic_cast<Bar&>`, and related casts now add `type_reference` rows for their target types.

## 日本語

- **C++ named cast の対象型を type reference として抽出するようになりました** — `static_cast<Foo*>`、`dynamic_cast<Bar&>` などの cast が対象型の `type_reference` 行を追加します。
