---
category: fixed
affected:
  - .github/workflows/release.yml
  - src/CodeIndex/Cli/JsonOutputContracts.cs
  - src/CodeIndex/Cli/ProgramRunner.cs
  - USER_GUIDE.md
  - DEVELOPER_GUIDE.md
  - CLOUD_BOOTSTRAP_PROMPT.md
  - tests/CodeIndex.Tests/CliJsonSerializerContextTests.cs
  - tests/CodeIndex.Tests/ProgramRunnerTests.cs
  - tests/CodeIndex.Tests/ReleaseWorkflowTests.cs
---

## English

- **Published trimmed self-contained binaries now support CLI JSON** — release artifacts are trimmed again, with every CLI JSON DTO routed through source-generated serializers so commands such as `cdidx status --json` work from the `install.sh` binary; the release verification step still asserts that JSON output succeeds.

## 日本語

- **公開 trim 済み self-contained バイナリで CLI JSON が使えるようにしました** — release artifact を再び trim しつつ、全 CLI JSON DTO を source-generated serializer 経路に載せたため、`install.sh` で入るバイナリでも `cdidx status --json` などが動作します。release verify step も JSON 出力の成功を検証します。
