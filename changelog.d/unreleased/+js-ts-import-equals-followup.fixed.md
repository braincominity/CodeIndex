---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **TypeScript `import foo = require(...)` lines now index both the alias and module path, even with trailing comments** — `SymbolExtractor` now treats import-equals declarations as searchable anchors for the local alias and the required module target, and it accepts same-line `//` or `/* ... */` comments after the statement terminator.

## 日本語

- **TypeScript の `import foo = require(...)` 行で alias と module path の両方を、行末コメント付きでも索引するようになりました** — `SymbolExtractor` は import-equals 宣言をローカル alias と require された module 先の両方に対する検索アンカーとして扱い、文末の `//` / `/* ... */` コメントも受け付けます。
