---
category: added
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.TypeReferences.cs
  - src/CodeIndex/Indexer/References/Languages/BroadLanguageReferenceExtractor.cs
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - src/CodeIndex/Database/DbReader.cs
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
  - DEVELOPER_GUIDE.md
---

## English

- **Expanded multi-language reference extraction** - `references`, `callers`, `callees`, and `impact` now include deeper language-specific graph edges for C/C++, Dart, Go, Elixir, Lua, Haskell, VB.NET, Razor/Blazor, Fortran, Pascal, Objective-C, and Smalltalk, including type-position references, imports/uses, component tags, message sends, parenless calls, constructors, and composite literals.

## 日本語

- **複数言語の参照抽出を拡張** - `references`、`callers`、`callees`、`impact` が C/C++、Dart、Go、Elixir、Lua、Haskell、VB.NET、Razor/Blazor、Fortran、Pascal、Objective-C、Smalltalk の言語固有 graph edge をより詳細に扱うようになりました。型位置参照、import / uses、component tag、message send、括弧なし呼び出し、constructor、composite literal などを含みます。
