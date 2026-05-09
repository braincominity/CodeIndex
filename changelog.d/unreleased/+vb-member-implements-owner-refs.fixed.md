---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic member `Implements IFoo.Member` reference extraction so the implemented interface owner is indexed.

## 日本語
- Visual Basic の member `Implements IFoo.Member` 参照抽出で、実装先 interface owner も索引するよう修正しました。
