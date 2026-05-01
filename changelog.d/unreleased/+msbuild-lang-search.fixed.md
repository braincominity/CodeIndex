---
category: fixed
affected:
  - src/CodeIndex/Indexer/FileIndexer.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **MSBuild project files now search as `msbuild` instead of generic `xml`** — `.csproj`, `.fsproj`, `.vbproj`, `.props`, and `.targets` files now keep their own language label, so `--lang msbuild` can target them directly without blending them into unrelated XML files.

## 日本語

- **MSBuild プロジェクトファイルが汎用 `xml` ではなく `msbuild` として検索されるようになりました** — `.csproj` / `.fsproj` / `.vbproj` / `.props` / `.targets` を独立した言語ラベルで保持するため、`--lang msbuild` でこれらのファイルを直接狙え、無関係な XML ファイルと混ざりません。
