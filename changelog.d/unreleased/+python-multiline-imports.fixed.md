---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python search now expands multiline parenthesized imports** — `from package import (...)` blocks now surface every imported name and alias across continuation lines, so search results match the identifiers used in the body of the import list instead of stopping at the opening line.

## 日本語

- **Python 検索が multiline の parenthesized import を展開するようになりました** — `from package import (...)` 形式でも、継続行にある imported name / alias をすべて拾うため、検索結果が import リスト本体の識別子に一致し、先頭行だけで止まらなくなります。
