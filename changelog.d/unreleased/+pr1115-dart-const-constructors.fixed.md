---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Dart bare `const` constructors are now indexed as part of the follow-up to PR #1115** — `cdidx` now recognizes `const` constructor declarations that do not use `this` / `super` initializers when they appear in Dart class bodies, while avoiding phantom symbols from `const` expressions outside member scope.

## 日本語

- **Dart の bare な `const` コンストラクタを PR #1115 の follow-up として索引するようになりました** — `cdidx` は Dart の class 本体内に現れる `this` / `super` 初期化子なしの `const` コンストラクタ宣言を認識し、member scope 外の `const` 式からは phantom symbol を作らないようになりました。
