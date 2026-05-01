---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran modules, submodules, and programs now keep body ranges** — `module`, `submodule`, and `program` declarations now track their matching `end` lines so internal procedures stay contained under the right parent symbol.

## 日本語

- **Fortran の module / submodule / program で body range を保持するようになりました** — `module`、`submodule`、`program` 宣言が対応する `end` 行まで範囲を追跡するため、内部手続きが正しい親シンボルの下に収まるようになります。
