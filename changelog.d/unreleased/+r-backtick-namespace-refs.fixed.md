---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.TypeReferences.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R namespace references now include backtick-quoted operator names** — `cdidx` records R namespace references such as `` pkg::`%>%` `` and `` pkg:::`%||%` `` as searchable references to both the qualified target and the operator leaf name.

## 日本語

- **R の namespace 参照で backtick 付き演算子名も拾うようになりました** — `cdidx` は `` pkg::`%>%` `` や `` pkg:::`%||%` `` のような R の namespace 参照を、修飾済みターゲットと演算子の leaf 名の両方に対する検索可能な参照として記録します。
