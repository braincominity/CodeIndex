---
category: changed
affected:
  - .github/workflows/release.yml
---

## English

- **Windows release artifacts can be published before Authenticode signing is configured** — the release workflow now skips Windows executable signing when the signing certificate secrets are absent, emits an explicit warning, and continues publishing unsigned Windows artifacts.

## 日本語

- **Authenticode 署名の設定前でも Windows release artifact を公開できるようにしました** — release workflow は署名証明書 secret が無い場合に Windows 実行ファイル署名をスキップし、明示的な warning を出したうえで未署名の Windows artifact 公開を継続します。
