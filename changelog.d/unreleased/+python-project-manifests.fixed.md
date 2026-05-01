---
category: fixed
affected:
  - src/CodeIndex/Indexer/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---

## English

- **Python project manifests now participate in Python search** — `pyproject.toml` and `requirements.txt` are recognized as `python`, so common project metadata files show up in Python search results and language listings instead of being skipped as generic text.

## 日本語

- **Python のプロジェクトマニフェストが Python 検索に参加するようになりました** — `pyproject.toml` と `requirements.txt` を `python` として認識するため、よく使われるプロジェクトメタデータファイルが汎用テキストとして取りこぼされず、Python の検索結果や言語一覧に含まれるようになります。
