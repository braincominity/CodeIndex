---
category: fixed
affected:
  - .github/workflows/release.yml
  - USER_GUIDE.md
  - DEVELOPER_GUIDE.md
  - CLOUD_BOOTSTRAP_PROMPT.md
  - tests/CodeIndex.Tests/ReleaseWorkflowTests.cs
---

## English

- **Published self-contained binaries now support CLI JSON** — release artifacts are published without trimming so commands such as `cdidx status --json` work from the `install.sh` binary, and the release verification step now asserts that JSON output succeeds.

## 日本語

- **公開 self-contained バイナリで CLI JSON が使えるようにしました** — release artifact を trim せずに publish することで、`install.sh` で入るバイナリでも `cdidx status --json` などが動作します。release verify step も JSON 出力の成功を検証するようにしました。
