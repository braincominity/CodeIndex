---
category: fixed
affected:
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/DockerfileReferenceExtractor.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Dockerfile stage aliases can include hyphens** — Dockerfile symbol and stage-reference extraction now indexes common aliases such as `build-env` in `FROM ... AS build-env`, `FROM build-env AS ...`, and `COPY --from=build-env`.
- **Suffix-style Dockerfile names are detected** — files named like `api.Dockerfile` or `worker.dockerfile` now index as `dockerfile` instead of falling into an unsupported extension bucket.
- **Suffix-style Containerfile names are detected** — files named like `api.Containerfile` or `worker.containerfile` now index through the Dockerfile analyzer.

## 日本語

- **Dockerfile の stage alias でハイフンを扱えるようになりました** — Dockerfile の symbol 抽出と stage 参照抽出が、`FROM ... AS build-env`、`FROM build-env AS ...`、`COPY --from=build-env` のような一般的な alias を index するようになりました。
- **suffix 型の Dockerfile 名を検出するようになりました** — `api.Dockerfile` や `worker.dockerfile` のようなファイルが unsupported extension ではなく `dockerfile` として index されるようになりました。
- **suffix 型の Containerfile 名を検出するようになりました** — `api.Containerfile` や `worker.containerfile` のようなファイルも Dockerfile analyzer で index されるようになりました。
