---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript default arrow exports are now searchable** — `export default () => value` and `export default async () => {}` now add exported `default` `function` symbols while default function, class, and object-literal exports stay on their existing extraction paths.

## 日本語

- **JavaScript/TypeScript の default arrow export を検索できるようになりました** — `export default () => value` や `export default async () => {}` が exported `default` `function` シンボルを追加し、default function / class / object-literal export は既存の抽出経路のまま扱われます。
