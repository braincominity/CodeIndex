---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/KotlinReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/JvmMethodReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Kotlin class literals now index their type target** — `User::class` and `User::class.java` now emit `type_reference` rows for `User`, matching C# `typeof(User)` and Java `User.class` behavior while avoiding raw `class` call noise.

## 日本語

- **Kotlin の class literal が対象型をインデックスするようになりました** — `User::class` と `User::class.java` が `User` への `type_reference` を出力し、C# の `typeof(User)` と Java の `User.class` と同じように検索できるようにしつつ、raw な `class` call ノイズは抑止します。
