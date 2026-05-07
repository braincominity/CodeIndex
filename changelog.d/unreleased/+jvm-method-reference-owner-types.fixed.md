---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/JvmMethodReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/JavaReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/ScalaReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Java and Kotlin method references now index owner types** — callable references such as `String::trim` and `User::name` now emit `type_reference` rows for their type-like owners while still avoiding lowercase receiver objects such as `xs::iterator`.

## 日本語

- **Java / Kotlin のメソッド参照で owner 型も検索できるようになりました** — `String::trim` や `User::name` のような callable reference で、型らしい owner を `type_reference` として発行します。一方で `xs::iterator` のような小文字 receiver object は除外します。
