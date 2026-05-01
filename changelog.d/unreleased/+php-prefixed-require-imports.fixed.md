---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **PHP prefixed `require` / `include` paths are now indexed as imports** — `require __DIR__ . '/bootstrap.php';` and similar prefixed literal forms are now preserved as searchable `import` symbols, so common bootstrap file paths no longer disappear behind directory constants.

## 日本語

- **PHP の prefixed な `require` / `include` パスも import として索引されるようになりました** — `require __DIR__ . '/bootstrap.php';` のようなディレクトリ定数付きの literal 形式も検索可能な `import` シンボルとして保持されるため、定番の bootstrap パスが directory constant の裏に消えなくなりました。
