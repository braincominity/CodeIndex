---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python class metaclasses emit type references** — `class Model(metaclass=orm.ModelMeta):` now records the metaclass so metaclass usage is searchable.

## 日本語

- **Python の metaclass 指定が型参照を出すようになりました** — `class Model(metaclass=orm.ModelMeta):` が metaclass を記録し、メタクラスの利用箇所を検索できるようになりました。
