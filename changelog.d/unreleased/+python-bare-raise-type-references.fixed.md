---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python bare `raise` statements emit type references** — `raise CustomError` now records `CustomError` as a `type_reference` so exception usage is searchable even without call parentheses.

## 日本語

- **Python の括弧なし `raise` が型参照を出すようになりました** — `raise CustomError` が `CustomError` を `type_reference` として記録し、呼び出し括弧がない例外利用も検索できるようになりました。
