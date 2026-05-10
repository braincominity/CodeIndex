---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PerlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Perl qualified call search no longer drops calls after barewords ending in `sub`** - `call_sub My::Util::format(...)` still emits the qualified call while `sub My::Util::format(...)` definitions remain ignored as calls.

## 日本語

- **Perl の qualified call 検索が `sub` で終わる bareword 後の呼び出しを落とさなくなりました** - `call_sub My::Util::format(...)` は qualified call を出力し、`sub My::Util::format(...)` 定義は引き続き call として扱いません。
