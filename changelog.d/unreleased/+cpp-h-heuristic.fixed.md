---
category: fixed
affected:
  - src/CodeIndex/Indexer/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
  - README.md
---

## English

- **C++-style `.h` headers now promote to `cpp` when the content makes it obvious** — `FileIndexer` keeps plain `.h` files on the C path by default, but upgrades headers that clearly contain C++ markers such as `namespace`, `template`, `using`, `class`, or `std::` so symbol and reference search uses the richer C++ extraction.

## 日本語

- **C++ らしい `.h` ヘッダーは内容から明白な場合に `cpp` へ昇格するようになりました** — `FileIndexer` は通常の `.h` を既定で C のまま扱いますが、`namespace`、`template`、`using`、`class`、`std::` などの C++ マーカーが明確なヘッダーは C++ 抽出を使うように切り替え、symbol / reference search を強化します。
