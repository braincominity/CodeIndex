---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB `Imports` targets now emit type references** — comma-separated and alias imports now contribute dependency references for imported namespaces or types.

## 日本語

- **VB の `Imports` 対象が type reference を出すようになりました** — comma 区切り import と alias import が、import 先 namespace / 型の依存 reference を生成します。
