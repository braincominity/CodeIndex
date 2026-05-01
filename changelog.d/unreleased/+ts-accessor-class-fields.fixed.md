---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **TypeScript auto-accessor class fields are now indexed as properties** — `accessor foo: T;` members inside classes are recognized as `property` symbols, which makes search and outline results include these modern TypeScript declarations.

## 日本語

- **TypeScript の auto-accessor クラスフィールドを property として索引化するようになりました** — クラス内の `accessor foo: T;` メンバーを `property` シンボルとして認識するため、検索や outline に最新の TypeScript 宣言が表示されるようになります。
