---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Dockerfile search now handles lowercase instructions and `ENV` declarations** — `cdidx` now recognizes `from` / `as` in addition to uppercase `FROM` / `AS`, and indexes `ENV` variable names alongside `ARG`, so modern Dockerfiles stay searchable regardless of instruction casing.

## 日本語

- **Dockerfile 検索が小文字の命令と `ENV` 宣言に対応しました** — `cdidx` は大文字の `FROM` / `AS` だけでなく `from` / `as` も認識し、`ARG` に加えて `ENV` の変数名も索引するようになったため、命令の大文字小文字に依存せず現代的な Dockerfile を検索できます。
