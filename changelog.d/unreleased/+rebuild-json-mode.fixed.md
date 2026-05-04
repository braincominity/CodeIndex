---
category: fixed
affected:
  - src/CodeIndex/Cli/IndexCommandRunner.cs
  - tests/CodeIndex.Tests/IndexCommandRunnerTests.cs
---

## English

- **`cdidx index --rebuild --json` now reports rebuild mode** — successful rebuild scans now emit `"mode": "rebuild"` instead of `"incremental"`.

## 日本語

- **`cdidx index --rebuild --json` が rebuild mode を返すようにしました** — rebuild scan 成功時の JSON が `"incremental"` ではなく `"rebuild"` を出力します。
