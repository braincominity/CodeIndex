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
- **C search now indexes `#include_next` targets** — GNU-style next-header directives now produce import symbols alongside ordinary `#include` rows.
- **C search now indexes `#import` headers** — import-style header directives now surface as import symbols for header navigation.

## 日本語

- **C 検索が空白付き include ディレクティブをインデックスするようになりました** — `# include <header.h>` 形式でも import シンボルを生成し、preprocessor 空白を使ったヘッダーも symbol/search navigation で見つかるようになりました。
- **C 検索が `#include_next` の参照先をインデックスするようになりました** — GNU 風の next-header ディレクティブも通常の `#include` と同じように import シンボルを生成します。
- **C 検索が `#import` ヘッダーをインデックスするようになりました** — import 形式のヘッダーディレクティブも import シンボルとして表面化し、ヘッダー移動に使えるようになりました。
