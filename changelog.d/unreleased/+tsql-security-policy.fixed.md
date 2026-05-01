---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Improved T-SQL DDL symbol coverage** — SQL symbol extraction now recognizes `CREATE SECURITY POLICY` and `ALTER SECURITY POLICY` declarations, so row-level security policy objects become searchable.

## 日本語

- **T-SQL DDL シンボル抽出を強化** — SQL シンボル抽出で `CREATE SECURITY POLICY` / `ALTER SECURITY POLICY` 宣言を認識するようにし、行レベルセキュリティポリシーのオブジェクトも検索可能にしました。
