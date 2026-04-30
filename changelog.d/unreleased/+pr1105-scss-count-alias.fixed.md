---
category: fixed
issues:
  - 1105
affected:
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SCSS `$` queries now stay scoped in `callers --count` / `callees --count` (#1105 follow-up)** — `CountCallersTotal` and `CountCalleesTotal` now preserve the leading `$` long enough to apply the CSS/SCSS alias branch, so `$primary` and `$rounded` continue to resolve only against CSS rows instead of broadening to unrelated names. Added a regression that mixes CSS and non-CSS rows with the same canonical names and verifies the count totals stay at the CSS-only values.

## 日本語

- **SCSS の `$` クエリが `callers --count` / `callees --count` でもスコープを保つようになりました (#1105 follow-up)** — `CountCallersTotal` と `CountCalleesTotal` が先頭の `$` を alias 判定まで保持するようになり、`$primary` と `$rounded` は CSS/SCSS 行だけに解決されて、無関係な名前へ広がらなくなりました。CSS 行と非 CSS 行に同じ canonical 名を混ぜた回帰テストを追加し、count total が CSS 限定の値のままであることを確認しています。
