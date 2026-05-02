---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/StructuralLineMasker.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Perl imports and POD sections are now handled more accurately** - `use` and `require` statements now index module dependencies as `import` symbols, while POD blocks are masked so documentation text in `*.pod` files does not leak fake packages or calls into search results.

## 日本語

- **Perl の import と POD セクションをより正確に扱うようになりました** - `use` / `require` 文は module dependency を `import` symbol として index し、POD ブロックはマスクすることで `*.pod` の文書テキストが偽の package / call として検索結果に漏れなくなります。
