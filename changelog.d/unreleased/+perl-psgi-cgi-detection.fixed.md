---
category: fixed
affected:
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---

## English

- **Perl PSGI and CGI entrypoints are now detected as Perl** - `.psgi`, `.cgi`, and `.fcgi` files now receive Perl indexing, symbol extraction, and reference extraction instead of falling back to unknown text.

## 日本語

- **Perl の PSGI / CGI エントリポイントを Perl として検出するようになりました** - `.psgi`、`.cgi`、`.fcgi` ファイルが unknown text ではなく Perl としてインデックス、symbol 抽出、reference 抽出されます。
