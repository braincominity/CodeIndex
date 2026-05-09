---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `NameOf(...)` reference extraction so the named target is indexed, including escaped target names, without treating `NameOf` itself as a call.

## 日本語
- Visual Basic の `NameOf(...)` 参照抽出で、エスケープ名を含む対象名を索引し、`NameOf` 自体を呼び出し扱いしないよう修正しました。
