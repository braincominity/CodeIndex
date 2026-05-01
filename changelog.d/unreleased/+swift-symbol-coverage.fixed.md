---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Expanded Swift symbol extraction coverage** — Swift indexing now captures `init`/`deinit`/`subscript` members, `associatedtype` declarations, stored `let`/`var` properties, `macro` declarations, operator/precedence definitions, package visibility, and dotted extension targets such as `Foundation.URLSession`.

## 日本語

- **Swift シンボル抽出の対応範囲を拡張** — Swift のインデックス作成で `init` / `deinit` / `subscript` メンバー、`associatedtype` 宣言、保存プロパティの `let` / `var`、`macro` 宣言、演算子/precedence 定義、`package` 可視性、`Foundation.URLSession` のようなドット区切り extension 対象を抽出できるようになりました。
