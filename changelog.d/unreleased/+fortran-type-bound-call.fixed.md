---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran type-bound `call object%method()` targets now resolve to the method** — explicit `call` statements with `%` receiver chains index the final procedure name instead of recording the receiver object as a phantom call.

## 日本語

- **Fortran の type-bound `call object%method()` が method を指すようになりました** — `%` receiver chain 付きの明示 `call` 文では receiver object の phantom call ではなく、最後の procedure 名を索引します。
