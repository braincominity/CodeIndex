---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Shell alias extraction now handles multiple alias definitions on one line** — shell parsing now expands additional `alias name=value` tokens that appear after the first alias command in the same statement, so definitions like `alias ll='ls -la' gs='git status'` are indexed correctly.

## 日本語

- **shell の alias 抽出で、1 行に複数書かれた定義も扱えるようになりました** — 同じ文の中に続けて書かれた追加の `alias name=value` トークンも展開し、`alias ll='ls -la' gs='git status'` のような定義を正しくインデックスします。
