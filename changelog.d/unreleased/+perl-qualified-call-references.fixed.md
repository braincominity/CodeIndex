---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PerlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Perl qualified function calls are now searchable by full name** - calls such as `My::Util::format(...)` now emit a `My::Util::format` call reference in addition to the leaf call.

## 日本語

- **Perl の qualified function call を完全修飾名で検索できるようになりました** - `My::Util::format(...)` のような呼び出しで、leaf call に加えて `My::Util::format` の call reference も出力します。
