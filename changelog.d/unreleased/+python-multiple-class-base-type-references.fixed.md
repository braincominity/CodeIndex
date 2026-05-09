---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PythonReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Python multiple class bases emit type references** — `class UserView(views.BaseView, mixins.AuditedMixin):` now records each base class for reference search.

## 日本語

- **Python の複数基底クラスが型参照を出すようになりました** — `class UserView(views.BaseView, mixins.AuditedMixin):` が各基底クラスを参照検索に記録するようになりました。
