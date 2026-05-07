---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/JvmMethodReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin backticked method-reference names now emit canonical call references** - ``User::`render name` `` now records the call as `render name`, matching the declaration symbol name.

## 日本語

- **Kotlin の backtick 付き method reference 名を canonical な call 参照として記録するようになりました** - ``User::`render name` `` で、宣言側の symbol 名と同じ `render name` を call として記録します。
