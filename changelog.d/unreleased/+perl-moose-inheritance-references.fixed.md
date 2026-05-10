---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PerlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Perl Moose inheritance declarations now show up in reference search** - `extends 'Module'` and `with qw(Role ...)` declarations are indexed as type references.

## 日本語

- **Perl Moose の継承宣言が参照検索に出るようになりました** - `extends 'Module'` と `with qw(Role ...)` 宣言を type reference としてインデックスします。
