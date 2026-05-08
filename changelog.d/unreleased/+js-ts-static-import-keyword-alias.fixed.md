---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **JavaScript/TypeScript static import signatures stay bounded when imported aliases use `with` or `assert`** — semicolonless declarations such as `import { with as alias } from "./module"` no longer confuse import-attributes detection while recording the source module.

## 日本語

- **JavaScript/TypeScript の静的 import で `with` / `assert` を alias として import しても signature 範囲が伸びなくなりました** — `import { with as alias } from "./module"` のような semicolon なし宣言でも、source module 記録時に import attributes 判定と混同しなくなりました。
