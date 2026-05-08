---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift function-type effects are no longer indexed as types** — `async`, `throws`, and `rethrows` are ignored in function type expressions while typed-throws errors and return types remain searchable.

## 日本語

- **Swift の関数型 effect を型として index しないようにしました** — 関数型式内の `async` / `throws` / `rethrows` を除外し、typed throws のエラー型や戻り値型は検索可能なままにします。
