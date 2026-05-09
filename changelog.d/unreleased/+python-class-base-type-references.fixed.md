---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python class bases emit type references** — `class UserView(views.BaseView):` now records `BaseView` as a `type_reference` so base-class usage is searchable from the base type.

## 日本語

- **Python の基底クラスが型参照を出すようになりました** — `class UserView(views.BaseView):` が `BaseView` を `type_reference` として記録し、基底型側から利用箇所を検索できるようになりました。
