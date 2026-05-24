#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

status=0

while IFS= read -r doc; do
  while IFS= read -r target; do
    [[ -z "$target" ]] && continue
    if [[ ! -s "$target" ]]; then
      printf '%s references missing or empty workflow file: %s\n' "$doc" "$target" >&2
      status=1
    fi
  done < <(perl -ne 'while (/`(\.codex\/workflows\/[^`\s)]*\.md)`/g) { print "$1\n" unless $1 =~ /[*?]/ }' "$doc" | sort -u)
done < <(
  {
    git ls-files '*.md'
    git ls-files '.codex/workflows/*.md'
  } | sort -u
)

exit "$status"
