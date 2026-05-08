---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift variadic generic `repeat` is no longer indexed as a type** — `TuplePack<repeat each Element>` now preserves the real `Element` reference without emitting `repeat` or `each` as phantom type references.

## 日本語

- **Swift variadic generic の `repeat` を型として index しないようにしました** — `TuplePack<repeat each Element>` で実際の `Element` 参照は残しつつ、`repeat` や `each` を phantom な型参照として出さないようにしました。
