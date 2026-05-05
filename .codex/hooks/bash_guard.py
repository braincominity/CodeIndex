#!/usr/bin/env python3
"""Codex PreToolUse Bash guard for Widthdom/CodeIndex."""

from __future__ import annotations

import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path


def load_core():
    repo_root = Path(__file__).resolve().parents[2]
    core_path = repo_root / ".agent_harness" / "command_guard_core.py"
    spec = importlib.util.spec_from_file_location("agent_harness.command_guard_core", core_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"could not load guard core from {core_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


core = load_core()


def load_payload() -> dict:
    try:
        return json.load(sys.stdin)
    except Exception:
        return {}


def get_command(payload: dict) -> str:
    tool_input = payload.get("tool_input") or {}
    command = tool_input.get("command")
    return command if isinstance(command, str) else ""


def resolve_project_root(cwd: Path) -> Path:
    try:
        proc = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            cwd=str(cwd),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=5,
            check=False,
        )
        if proc.returncode == 0:
            output = (proc.stdout or "").strip()
            if output:
                return Path(output).resolve()
    except Exception:
        pass

    return cwd.resolve()


def deny(reason: str) -> None:
    print(
        json.dumps(
            {
                "hookSpecificOutput": {
                    "hookEventName": "PreToolUse",
                    "permissionDecision": "deny",
                    "permissionDecisionReason": reason,
                }
            },
            ensure_ascii=False,
        )
    )
    sys.exit(2)


def main() -> None:
    payload = load_payload()
    command = get_command(payload)
    cwd = Path(payload.get("cwd") or os.getcwd()).resolve()
    project_root = resolve_project_root(cwd)

    decision = core.evaluate_bash_command(command, cwd=cwd, project_root=project_root)
    if not decision.allowed:
        deny(decision.reason)

    for script in core.candidate_script_paths(command, cwd):
        if core.should_skip_script_scan(decision, script, project_root):
            continue
        script_decision = core.check_script_file(script, project_root)
        if not script_decision.allowed:
            deny(script_decision.reason)

    if re.search(r"(?i)(^|[\s;&|()`])git\s+commit\b", command):
        commit_decision = core.staged_secret_check(cwd)
        if not commit_decision.allowed:
            deny(commit_decision.reason)

    sys.exit(0)


if __name__ == "__main__":
    main()
