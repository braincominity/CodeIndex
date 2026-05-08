---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/JvmMethodReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **JVM method references now accept generic owner types** - Java references such as `Box<String>::new` and `Box<String>::open` are indexed against `Box` / `open` instead of being missed or carrying generic suffix noise.

## 日本語

- **JVM method reference が generic owner type を扱えるようになりました** - `Box<String>::new` や `Box<String>::open` のような Java 参照を見逃さず、generic suffix ノイズなしで `Box` / `open` に紐づけます。
