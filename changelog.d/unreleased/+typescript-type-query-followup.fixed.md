---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **TypeScript type-expression scanning now skips template raw text and string keys while keeping real identifiers** - follow-up coverage from PR #1245 now ignores the literal text inside template-literal types and indexed-access string keys, but still records the type names inside `${...}` holes and other genuine type references.

## 日本語

- **TypeScript の型式走査が template literal の raw text と文字列キーを飛ばし、本物の識別子だけを残すようになりました** - PR #1245 の follow-up として、template literal 型の中の文字面や indexed access の文字列キーは無視しつつ、`${...}` の穴や他の本物の型参照はそのまま記録します。
