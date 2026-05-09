---
category: changed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C search now indexes spaced include directives** — `# include <header.h>` forms now produce import symbols, so symbol/search navigation sees headers written with preprocessor whitespace.

## 日本語

- **C 検索が空白付き include ディレクティブをインデックスするようになりました** — `# include <header.h>` 形式でも import シンボルを生成し、preprocessor 空白を使ったヘッダーも symbol/search navigation で見つかるようになりました。
