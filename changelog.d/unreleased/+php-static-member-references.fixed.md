---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP static constants and enum cases now emit member references** — `Config::VERSION` and `Priority::Low` now index `VERSION` and `Low` as references while method calls such as `Config::rebuild()` remain calls only.

## 日本語

- **PHP の static 定数と enum case をメンバー参照として索引するようになりました** — `Config::VERSION` や `Priority::Low` で `VERSION` / `Low` を reference として出し、`Config::rebuild()` のようなメソッド呼び出しは call のままにします。
