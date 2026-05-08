---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Swift type parameter modifiers are no longer indexed as types** — `inout`, `isolated`, `consuming`, `sending`, and `borrowing` are ignored in type positions while keeping the real parameter types searchable.

## 日本語

- **Swift の型パラメータ修飾子を型として index しないようにしました** — `inout` / `isolated` / `consuming` / `sending` / `borrowing` を型位置から除外し、実際のパラメータ型は検索可能なままにします。
