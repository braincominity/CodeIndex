---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/JavaReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Java generic heritage clauses no longer emit type-parameter self references** - declarations such as `class Box<T> extends Base<T> implements Handler<T>` now keep `Base` / `Handler` while suppressing `T` noise.

## 日本語

- **Java の generic heritage 句が型パラメータ自身を型参照として出さないようになりました** - `class Box<T> extends Base<T> implements Handler<T>` では `Base` / `Handler` を残しつつ、`T` のノイズを抑制します。
