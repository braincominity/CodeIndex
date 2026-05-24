# GitHub Actions Supply Chain Policy

GitHub Actions workflow steps that use third-party actions must pin the action
to a full 40-character commit SHA, with the human-readable release tag kept in
a trailing comment:

```yaml
uses: actions/checkout@34e114876b0b11c390a56381ad16ebd13914f8d5 # v4.3.1
```

Do not use mutable tags such as `@v4`, `@v3`, or branch names for third-party
actions. Mutable references can be retagged upstream without a repository diff,
which lets changed action code run in CI or release workflows without review.

Dependabot is configured for the `github-actions` ecosystem in
`.github/dependabot.yml`. Let Dependabot open update pull requests so action
SHA changes are reviewed like source changes.
