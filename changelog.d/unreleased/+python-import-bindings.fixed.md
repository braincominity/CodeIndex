---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python import search now includes local bindings and imported names** — `import numpy as np` now surfaces both `numpy` and `np`, and `from ... import ...` statements surface the imported names and aliases that Python code actually binds, so search results match the identifiers developers use in code.

## 日本語

- **Python の import 検索で local binding と imported name も拾うようになりました** — `import numpy as np` では `numpy` と `np` の両方を、`from ... import ...` では Python コードが実際に束縛する imported name / alias を拾うため、検索結果がコード中で使う識別子と一致しやすくなります。
