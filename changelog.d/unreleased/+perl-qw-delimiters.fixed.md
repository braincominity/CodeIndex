---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PerlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Perl inheritance searches now recognize common `qw` delimiters** - `use parent` and `use base` now extract type references from `qw[...]`, `qw{...}`, `qw/.../`, and `qw<...>` lists in addition to `qw(...)`.

## 日本語

- **Perl の継承検索が一般的な `qw` 区切りに対応しました** - `use parent` と `use base` は従来の `qw(...)` に加えて、`qw[...]`、`qw{...}`、`qw/.../`、`qw<...>` のリストからも type reference を抽出します。
