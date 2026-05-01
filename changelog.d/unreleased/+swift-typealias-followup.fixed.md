---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Swift `typealias` extraction now handles backtick-escaped names and `where` clauses** — `typealias` declarations no longer fall back to `import` when the alias name is escaped or followed by a constraint clause, and the CLI now has end-to-end coverage for `symbols --lang swift --kind typealias` and `--kind associatedtype`.

## 日本語

- **Swift の `typealias` 抽出がバッククォート付き識別子と `where` 句に対応しました** — `typealias` 宣言は、別名がバッククォートでエスケープされている場合や制約句が付く場合でも `import` にフォールバックせず、CLI には `symbols --lang swift --kind typealias` と `--kind associatedtype` の end-to-end カバレッジも追加しました。
