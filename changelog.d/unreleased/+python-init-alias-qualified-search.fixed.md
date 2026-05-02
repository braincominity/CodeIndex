---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Python `__init__.py` alias re-exports now index qualified package names** — when a package initializer re-exports a module or alias with `import` / `from . import ... as ...`, cdidx now adds `package.alias`-style names alongside the leaf symbol so exact-name search can find those public re-exports.

## 日本語

- **Python の `__init__.py` における alias 再エクスポートは修飾済み package 名でも索引されます** — package initializer が `import` や `from . import ... as ...` で module や alias を再エクスポートしている場合、cdidx は leaf symbol に加えて `package.alias` 形式の名前も追加するため、exact-name 検索で公開 re-export を見つけやすくなります。
