---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/JvmMethodReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin backticked method-reference owners now emit canonical type references** - `` `Display Name`::render `` now records `Display Name` as the owner type dependency while still indexing the `render` call.

## 日本語

- **Kotlin の backtick 付き method reference owner を canonical な型参照として記録するようになりました** - `` `Display Name`::render `` で、`render` の call に加えて owner 型 `Display Name` を依存として記録します。
