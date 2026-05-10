---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PerlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Perl string `require` paths are now searchable as module references** - `require "Foo/Bar.pm"` is indexed as a `Foo::Bar` module reference.

## 日本語

- **Perl の文字列 `require` パスを module reference として検索できるようになりました** - `require "Foo/Bar.pm"` を `Foo::Bar` の module reference としてインデックスします。
