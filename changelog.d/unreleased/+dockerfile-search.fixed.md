---
category: fixed
affected:
  - src/CodeIndex/Indexer/Scanning/FileIndexer.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/DockerfileReferenceExtractor.cs
  - tests/CodeIndex.Tests/FileIndexerTests.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Dockerfile stage aliases can include hyphens** — Dockerfile symbol and stage-reference extraction now indexes common aliases such as `build-env` in `FROM ... AS build-env`, `FROM build-env AS ...`, and `COPY --from=build-env`.
- **Suffix-style Dockerfile names are detected** — files named like `api.Dockerfile` or `worker.dockerfile` now index as `dockerfile` instead of falling into an unsupported extension bucket.
- **Suffix-style Containerfile names are detected** — files named like `api.Containerfile` or `worker.containerfile` now index through the Dockerfile analyzer.
- **Hidden `.dockerfile` files are detected** — extensionless hidden Dockerfile variants now index as Dockerfile content instead of falling through to shebang probing.
- **Hidden `.containerfile` files are detected** — hidden Containerfile variants now use the Dockerfile indexing path.
- **Hyphen-suffixed Dockerfile names are detected** — files such as `Dockerfile-prod` now index as Dockerfile content.
- **Hyphen-suffixed Containerfile names are detected** — files such as `Containerfile-prod` now index through the Dockerfile analyzer.
- **Underscore-suffixed Dockerfile names are detected** — files such as `Dockerfile_prod` now index as Dockerfile content.
- **Underscore-suffixed Containerfile names are detected** — files such as `Containerfile_prod` now index through the Dockerfile analyzer.
- **Dockerfile stage aliases can include dots** — aliases such as `build.env` now stay intact in stage symbols and `FROM` / `COPY --from` references.
- **Commented `FROM` stage reuse lines keep references** — `FROM builder AS runtime # comment` now indexes the `builder` stage dependency.
- **Dockerfile braced ARG/ENV uses become references** — `${NODE_VERSION}` now links back to the indexed `ARG NODE_VERSION` / `ENV NODE_VERSION` property symbol.
- **Dockerfile defaulted braced variables become references** — `${NODE_VERSION:-20}` now links back to `NODE_VERSION` instead of being skipped.
- **Dockerfile unbraced ARG/ENV uses become references** — `$APP_HOME` now links back to the indexed `ARG APP_HOME` / `ENV APP_HOME` property symbol.
- **Escaped Dockerfile dollars stay literal** — `\$APP_HOME` no longer creates a false reference to `APP_HOME`.
- **Escaped Dockerfile braced variables stay literal** — `\${APP_HOME}` no longer creates a false reference to `APP_HOME`.

## 日本語

- **Dockerfile の stage alias でハイフンを扱えるようになりました** — Dockerfile の symbol 抽出と stage 参照抽出が、`FROM ... AS build-env`、`FROM build-env AS ...`、`COPY --from=build-env` のような一般的な alias を index するようになりました。
- **suffix 型の Dockerfile 名を検出するようになりました** — `api.Dockerfile` や `worker.dockerfile` のようなファイルが unsupported extension ではなく `dockerfile` として index されるようになりました。
- **suffix 型の Containerfile 名を検出するようになりました** — `api.Containerfile` や `worker.containerfile` のようなファイルも Dockerfile analyzer で index されるようになりました。
- **hidden 形式の `.dockerfile` を検出するようになりました** — 拡張子を持たない hidden Dockerfile 変種も shebang 判定に落ちず、Dockerfile content として index されるようになりました。
- **hidden 形式の `.containerfile` を検出するようになりました** — hidden Containerfile 変種も Dockerfile の index 経路を使うようになりました。
- **ハイフン suffix の Dockerfile 名を検出するようになりました** — `Dockerfile-prod` のようなファイルも Dockerfile content として index されるようになりました。
- **ハイフン suffix の Containerfile 名を検出するようになりました** — `Containerfile-prod` のようなファイルも Dockerfile analyzer で index されるようになりました。
- **underscore suffix の Dockerfile 名を検出するようになりました** — `Dockerfile_prod` のようなファイルも Dockerfile content として index されるようになりました。
- **underscore suffix の Containerfile 名を検出するようになりました** — `Containerfile_prod` のようなファイルも Dockerfile analyzer で index されるようになりました。
- **Dockerfile の stage alias で dot を扱えるようになりました** — `build.env` のような alias が stage symbol と `FROM` / `COPY --from` 参照で欠けずに保持されるようになりました。
- **コメント付きの `FROM` stage 再利用行でも参照を保持するようになりました** — `FROM builder AS runtime # comment` が `builder` stage dependency として index されるようになりました。
- **Dockerfile の braced ARG/ENV 利用を参照として扱うようになりました** — `${NODE_VERSION}` が index 済みの `ARG NODE_VERSION` / `ENV NODE_VERSION` property symbol に結びつくようになりました。
- **Dockerfile の default 付き braced variable を参照として扱うようになりました** — `${NODE_VERSION:-20}` が skip されず `NODE_VERSION` に結びつくようになりました。
- **Dockerfile の unbraced ARG/ENV 利用を参照として扱うようになりました** — `$APP_HOME` が index 済みの `ARG APP_HOME` / `ENV APP_HOME` property symbol に結びつくようになりました。
- **escape された Dockerfile の dollar を literal として扱うようになりました** — `\$APP_HOME` が `APP_HOME` への誤参照を作らなくなりました。
- **escape された Dockerfile の braced variable を literal として扱うようになりました** — `\${APP_HOME}` が `APP_HOME` への誤参照を作らなくなりました。
