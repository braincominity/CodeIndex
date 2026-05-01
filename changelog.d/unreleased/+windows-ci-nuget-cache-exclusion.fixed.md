---
category: fixed
affected:
  - .github/workflows/dotnet.yml
  - .github/workflows/release.yml
---

## English

- **Windows CI now excludes the immutable NuGet global package cache from Defender scanning** — the build-and-test and release workflows now add the restored package cache paths to the Windows Defender exclusion list alongside workspace and temp roots, which reduces repeated scanning of restore/build inputs on Windows runners.

## 日本語

- **Windows CI で変更されない NuGet global package cache も Defender のスキャン対象から外すようになりました** — build-and-test と release の両 workflow で、workspace と temp root に加えて復元済みパッケージ cache のパスも Windows Defender の除外リストへ追加し、Windows runner 上で restore / build 入力が繰り返しスキャンされるのを減らします。
