---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/JvmMethodReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Java array constructor method references now point at the component type** - `Widget[]::new` and `demo.Widget[][]::new` are indexed as instantiations of `Widget` / `demo.Widget` instead of missing the edge or keeping array suffix noise.

## 日本語

- **Java の配列コンストラクタ method reference が要素型を指すようになりました** - `Widget[]::new` や `demo.Widget[][]::new` を、配列 suffix ノイズではなく `Widget` / `demo.Widget` の instantiate として索引化します。
