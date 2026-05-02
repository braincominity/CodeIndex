---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Perl package and subroutine symbols are now searchable** - `perl` files now index `package Foo::Bar;` as namespaces and `sub name { ... }` as functions, and Perl is included in reference extraction so `#` comments no longer leak false call hits.

## 日本語

- **Perl の package と subroutine が検索できるようになりました** - `perl` ファイルでは `package Foo::Bar;` を namespace、`sub name { ... }` を function として index し、reference extraction にも Perl を追加したので `#` コメントから誤った call が漏れなくなります。
