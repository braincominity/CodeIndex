---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - README.md
---

## English

- **Shell command-style function calls are now indexed** — `cdidx` now recognizes same-file shell function calls written in bare command syntax, so shell call graphs include edges such as `setup && cleanup` and `if setup; then ...`.

## 日本語

- **Shell のコマンド構文による関数呼び出しを索引するようになりました** — `cdidx` は同一ファイル内で定義された shell function の bare command 呼び出しを認識し、`setup && cleanup` や `if setup; then ...` のような edge も call graph に含めます。
