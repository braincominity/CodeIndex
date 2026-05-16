#!/usr/bin/env python3
"""Summarize a cdidx --metrics JSONL file.

Reports count, p50, p95, p99, and max elapsed_ms per (source, tool) pair so you
can spot latency regressions or throughput drops from a captured log without
re-running queries. Pass the JSONL path as the only argument; reads from stdin
when no argument is provided.

cdidx の --metrics JSONL ファイルを集計するサンプルスクリプト。
(source, tool) 別に count / p50 / p95 / p99 / max の elapsed_ms を表示する。
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Iterable


def _percentile(sorted_values: list[float], pct: float) -> float:
    if not sorted_values:
        return math.nan
    if len(sorted_values) == 1:
        return sorted_values[0]
    k = (len(sorted_values) - 1) * (pct / 100.0)
    lower = math.floor(k)
    upper = math.ceil(k)
    if lower == upper:
        return sorted_values[int(k)]
    return sorted_values[lower] + (sorted_values[upper] - sorted_values[lower]) * (k - lower)


def _iter_records(stream: Iterable[str]) -> Iterable[dict]:
    for raw in stream:
        line = raw.strip()
        if not line:
            continue
        try:
            yield json.loads(line)
        except json.JSONDecodeError as exc:
            print(f"warn: skipping malformed line: {exc}", file=sys.stderr)


def summarize(records: Iterable[dict]) -> list[dict]:
    buckets: dict[tuple[str, str], list[float]] = defaultdict(list)
    for rec in records:
        elapsed = rec.get("elapsed_ms")
        if not isinstance(elapsed, (int, float)):
            continue
        key = (rec.get("source") or "?", rec.get("tool") or "?")
        buckets[key].append(float(elapsed))

    rows: list[dict] = []
    for (source, tool), values in sorted(buckets.items()):
        values.sort()
        rows.append({
            "source": source,
            "tool": tool,
            "count": len(values),
            "p50_ms": round(_percentile(values, 50), 3),
            "p95_ms": round(_percentile(values, 95), 3),
            "p99_ms": round(_percentile(values, 99), 3),
            "max_ms": round(values[-1], 3),
        })
    return rows


def _print_table(rows: list[dict]) -> None:
    if not rows:
        print("(no records)")
        return
    headers = ["source", "tool", "count", "p50_ms", "p95_ms", "p99_ms", "max_ms"]
    widths = [max(len(h), *(len(str(r[h])) for r in rows)) for h in headers]
    fmt = "  ".join(f"{{:<{w}}}" for w in widths)
    print(fmt.format(*headers))
    print(fmt.format(*("-" * w for w in widths)))
    for r in rows:
        print(fmt.format(*(r[h] for h in headers)))


def main() -> int:
    parser = argparse.ArgumentParser(description="Summarize cdidx --metrics JSONL output")
    parser.add_argument("path", nargs="?", help="Path to JSONL file (defaults to stdin)")
    parser.add_argument("--json", action="store_true", help="Emit summary rows as JSON")
    args = parser.parse_args()

    if args.path and args.path != "-":
        with Path(args.path).open("r", encoding="utf-8") as fh:
            rows = summarize(_iter_records(fh))
    else:
        rows = summarize(_iter_records(sys.stdin))

    if args.json:
        json.dump(rows, sys.stdout, indent=2)
        sys.stdout.write("\n")
    else:
        _print_table(rows)
    return 0


if __name__ == "__main__":
    sys.exit(main())
