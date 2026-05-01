---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Python dotted imports now surface prefix symbols for better searchability** — `import package.submodule` and `from package.subpackage import helper` now also emit the intermediate prefix names such as `package`, so exact symbol searches can find the owning file from either the full module path or its package prefix. Added regression coverage for symbol extraction and a CLI `symbols --exact-name` check.

## 日本語

- **Python のドット区切り import で prefix シンボルも出るようになり、検索しやすくなりました** — `import package.submodule` や `from package.subpackage import helper` で、中間の `package` のような prefix 名も併せて出力するため、完全な module path でも package prefix でも `symbols --exact-name` から該当ファイルを見つけやすくなります。SymbolExtractor の回帰テストと CLI の `symbols --exact-name` 検証を追加しました。
