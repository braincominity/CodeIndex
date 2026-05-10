---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP class properties are now searchable as symbols** — `public`, `protected`, `private`, and legacy `var` property declarations now appear as `property` symbols, so member-access references can navigate back to declarations.

## 日本語

- **PHP のクラスプロパティをシンボルとして検索できるようになりました** — `public`、`protected`、`private`、旧式の `var` プロパティ宣言を `property` シンボルとして出すため、メンバーアクセス参照から宣言へ辿りやすくなりました。
