---
category: fixed
affected:
  - src/CodeIndex/Indexer/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
---

## English

- **MSBuild project files now participate in hotspot-family marker detection** — the hotspot-family marker path treats `.csproj`, `.fsproj`, `.vbproj`, `.props`, and `.targets` as a first-class `msbuild` family so project-scope fingerprinting and scope keys follow MSBuild project structure instead of falling back to generic heuristics.

## 日本語

- **MSBuild のプロジェクトファイルが hotspot-family の marker 判定に参加するようになりました** — hotspot-family の marker 判定で `.csproj`、`.fsproj`、`.vbproj`、`.props`、`.targets` を第一級の `msbuild` ファミリーとして扱うため、project-scope の fingerprint と scope key が一般的なヒューリスティックではなく MSBuild の構成に沿って決まるようになります。
