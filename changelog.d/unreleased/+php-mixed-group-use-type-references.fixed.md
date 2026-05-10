---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP mixed group imports now keep function and const entries out of type references** — `use App\{User, function make_user, const ROLE};` records `User` as a type reference without emitting function or const imports as types.

## 日本語

- **PHP の mixed group import で function / const 要素を型参照から除外するようになりました** — `use App\{User, function make_user, const ROLE};` は `User` だけを type reference として記録し、function / const import を型として出さなくなります。
