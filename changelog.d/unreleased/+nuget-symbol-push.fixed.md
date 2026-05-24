---
category: fixed
affected:
  - .github/workflows/release.yml
---

## English

- **NuGet publishing no longer pushes symbol packages twice** - the release workflow now disables automatic symbol publishing during the main package push and uploads the `.snupkg` explicitly once, avoiding the NuGet 409 conflict after a successful package publish.

## 日本語

- **NuGet 公開で symbol package を二重 push しないようになりました** - release workflow はメイン package の push 時に自動 symbol 公開を無効化し、`.snupkg` を明示的に 1 回だけ upload するため、package 公開成功後の NuGet 409 conflict を回避します。
