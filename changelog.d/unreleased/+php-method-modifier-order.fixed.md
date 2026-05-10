---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP method symbols tolerate modifier order used in real code** — declarations such as `abstract protected function normalize()` and `final public static function make()` are now indexed as functions with visibility preserved.

## 日本語

- **PHP メソッドシンボルが実コードの修飾子順序に対応しました** — `abstract protected function normalize()` や `final public static function make()` のような宣言を、visibility を保った function として索引します。
