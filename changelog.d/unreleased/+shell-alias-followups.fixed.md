---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Shell alias follow-up coverage now includes hyphenated names and zsh global aliases** — shell alias definitions now accept hyphenated names, command-style lookup recognizes those aliases, and zsh `alias -g` uses are indexed even when they appear outside command-head positions.

## 日本語

- **Shell alias のフォローアップ対応として、ハイフン入り名と zsh の global alias を扱えるようになりました** — shell の alias 定義でハイフン入りの名前を許可し、コマンド構文の lookup でもその alias を認識するようにしました。さらに zsh の `alias -g` は command-head 以外の位置でもインデックスされます。
