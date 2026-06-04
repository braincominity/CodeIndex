---
category: fixed
affected:
  - src/CodeIndex/Database/DbWriter.cs
  - src/CodeIndex/Database/DbReader.FilesStatus.cs
  - src/CodeIndex/Database/JsonStringListCodec.cs
  - .github/workflows/release.yml
---

## English

- **Trimmed release indexing no longer hits reflection-based JSON metadata** — unknown-extension status metadata now uses a reflection-free string-list JSON codec, so the published self-contained release can complete `cdidx .` and the release installer smoke test can keep verifying `status --json`.

## 日本語

- **trimmed release の index が reflection-based JSON metadata に触れて失敗しなくなりました** — unknown-extension の status metadata は reflection を使わない string-list JSON codec で読み書きするようになり、公開済み self-contained release で `cdidx .` が完了し、release installer smoke test でも `status --json` の検証を継続できます。
