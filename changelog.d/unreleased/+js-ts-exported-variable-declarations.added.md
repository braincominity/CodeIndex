---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript exported variable declarations are now searchable** — `export const foo = 1, bar = 2`, `export let`, `export var`, and TypeScript `export declare const` declarations now add exported `property` symbols for the public variable names without duplicating exported function bindings.

## 日本語

- **JavaScript/TypeScript の exported variable declaration を検索できるようになりました** — `export const foo = 1, bar = 2`、`export let`、`export var`、TypeScript の `export declare const` が公開変数名を exported `property` シンボルとして追加し、export 済み関数束縛は重複させません。
