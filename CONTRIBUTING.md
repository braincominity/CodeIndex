# Contributing

Thanks for improving CodeIndex.

## Licensing of Contributions

By submitting a pull request, patch, issue attachment, or other contribution,
you agree that your contribution is provided under the same license that applies
to the file or directory you are contributing to.

In general:

- contributions to the protected implementation are licensed under
  `FSL-1.1-ALv2`;
- contributions to Apache-2.0 integration examples or integration docs are
  licensed under `Apache-2.0`;
- if a file has an explicit SPDX-License-Identifier, that identifier controls.

## Commercial Licensing Note

CodeIndex may offer separate written agreements for competing-product use.
Do not submit contributions unless you are comfortable with the repository's
licensing and commercial-use policy.

If this project later needs a formal contributor license agreement, add it in a
separate explicit change. Do not silently convert this contribution policy into
a copyright assignment.

## Trademarks

Contributions do not grant rights to use the CodeIndex or cdidx names for
derivative products. See `TRADEMARKS.md`.

## Development

Run:

```bash
dotnet restore CodeIndex.sln
dotnet build CodeIndex.sln -c Release
dotnet test CodeIndex.sln -c Release
```
