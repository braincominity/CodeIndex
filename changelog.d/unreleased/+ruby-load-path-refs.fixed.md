---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `load` paths are now indexed as references** — `cdidx` records the string path in `load "file.rb"` so dynamically loaded Ruby files are easier to find.

## 日本語

- **Ruby の `load` パスを参照として索引するようになりました** — `cdidx` は `load "file.rb"` の文字列パスを記録するため、動的に読み込まれる Ruby ファイルを見つけやすくなります。
