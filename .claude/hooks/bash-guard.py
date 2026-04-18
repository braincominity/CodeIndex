#!/usr/bin/env python3
"""
Claude Code PreToolUse Bash guard for Widthdom/CodeIndex.

Purpose:
- force CodeIndex dogfooding through the locally built cdidx.dll
- block shell grep/file-discovery escape hatches
- block destructive/exfiltrating commands
- inspect script files before execution so scripts cannot hide grep/dangerous calls
- scan staged changes before git commit to reduce API key / secret accidents
"""

from __future__ import annotations

import json
import os
import re
import shlex
import shutil
import subprocess
import sys
from pathlib import Path


MAX_SCRIPT_SCAN_BYTES = 512 * 1024
LOCAL_CDIDX_REL = Path("src/CodeIndex/bin/Debug/net8.0/cdidx.dll")

SHELL_CONTROL_RE = re.compile(r"(?s)(?:&&|\|\||;|\|&|\||&|`|\$\(|<|>|\n)")

SEARCH_OR_DISCOVERY = re.compile(
    r"""(?ix)
    (^|[\s;&|()`])
    (
      grep|egrep|fgrep|zgrep|rgrep|
      rg|ripgrep|ag|ack|ack-grep|
      find|fd|fdfind|locate|mlocate|mdfind
    )
    (?=\s|$)
    """
)

GLOBAL_CDIDX = re.compile(r"(?i)(^|[\s;&|()`/])cdidx(?!\.dll)(?=\s|$)")
GIT_GREP = re.compile(r"(?i)(^|[\s;&|()`])git\s+grep\b")

DANGEROUS_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (SEARCH_OR_DISCOVERY, "shell search/file-discovery command is blocked; use the local cdidx.dll"),
    (GLOBAL_CDIDX, "global cdidx is blocked; use dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll"),
    (GIT_GREP, "git grep is blocked; use the local cdidx.dll"),

    (re.compile(r"(?i)\brm\s+-[^\n;|&]*r[^\n;|&]*f\b|\brm\s+-[^\n;|&]*f[^\n;|&]*r\b"), "recursive forced rm is blocked"),
    (re.compile(r"(?i)\brm\s+-r\b"), "recursive rm is blocked"),
    (re.compile(r"(?i)\b(?:rmdir|unlink|shred|srm|truncate)\b"), "destructive filesystem command is blocked"),
    (re.compile(r"(?i)\bdd\s+(?:if|of)="), "dd raw device/file copy is blocked"),
    (re.compile(r"(?i)\b(?:mkfs|newfs)\b"), "filesystem formatting is blocked"),
    (re.compile(r"(?i)\bdiskutil\s+(?:erase|partition|apfs\s+delete)\b"), "disk erase/partition operation is blocked"),
    (re.compile(r"(?i)\bchmod\s+(?:777|-R)\b|\bchown\s+-R\b|\bchgrp\s+-R\b"), "dangerous recursive permission/owner change is blocked"),
    (re.compile(r"(?i)\b(?:sudo|su|doas)\b"), "privilege escalation is blocked"),
    (re.compile(r"(?i)\b(?:killall|pkill)\b|\bkill\s+-9\b"), "broad process killing is blocked"),

    (re.compile(r"(?i)\b(?:curl|wget|http|https|xh|aria2c)\b"), "network download/exfil command is blocked"),
    (re.compile(r"(?i)\b(?:curl|wget)\b.*\|\s*(?:sh|bash|zsh|python|ruby|perl)\b"), "download-and-execute is blocked"),
    (re.compile(r"(?i)\b(?:ssh|scp|sftp|rsync|rclone|nc|ncat|netcat|socat|telnet|ftp)\b"), "remote shell/file transfer is blocked"),
    (re.compile(r"(?i)\b(?:pbcopy|pbpaste)\b"), "clipboard access is blocked"),

    (re.compile(r"(?i)\b(?:open|osascript|automator)\b|\bshortcuts\s+run\b"), "macOS automation/app launching is blocked"),
    (re.compile(r"(?i)\b(?:launchctl|security|tccutil|spctl|csrutil|tmutil)\b"), "macOS security/system command is blocked"),
    (re.compile(r"(?i)\bdefaults\s+write\b|\bplutil\s+-replace\b"), "macOS preference modification is blocked"),

    (re.compile(r"(?i)\bgit\s+push\b"), "git push is blocked"),
    (re.compile(r"(?i)\bgit\s+tag\b"), "git tag is blocked unless explicitly performed by the user"),
    (re.compile(r"(?i)\bgit\s+reset\s+--hard\b"), "git reset --hard is blocked"),
    (re.compile(r"(?i)\bgit\s+(?:checkout|restore)\s+\.\b"), "checkout/restore of entire worktree is blocked"),
    (re.compile(r"(?i)\bgit\s+clean\s+-[^\n;|&]*f\b"), "git clean -f is blocked"),
    (re.compile(r"(?i)\bgit\s+add\s+(?:\.|-A|--all)\b"), "bulk git add is blocked; add explicit safe files only"),
    (re.compile(r"(?i)\bgit\s+commit\s+--amend\b|\bgit\s+rebase\b|\bgit\s+filter-branch\b|\bgit\s+update-ref\b"), "history rewriting is blocked"),

    (re.compile(r"(?i)\b(?:npm|yarn|pnpm)\s+publish\b|\bdotnet\s+nuget\s+push\b|\bnuget\s+push\b"), "package publishing is blocked"),
    (re.compile(r"(?i)\b(?:npm\s+login|npm\s+adduser)\b"), "package registry login is blocked"),
    (re.compile(r"(?i)\b(?:npx|npm\s+exec|yarn\s+dlx|pnpm\s+dlx)\b"), "ephemeral package execution is blocked"),
    (re.compile(r"(?i)\b(?:terraform\s+(?:apply|destroy)|kubectl\s+(?:apply|delete)|helm\s+(?:install|upgrade|uninstall))\b"), "infra mutation is blocked"),
    (re.compile(r"(?i)\bdocker\s+(?:push|login|system\s+prune|volume\s+rm|rm|rmi)\b|\bdocker\s+buildx\s+build\b.*--push\b"), "dangerous docker operation is blocked"),
    (re.compile(r"(?i)\b(?:aws|gcloud|az)\b"), "cloud CLI is blocked"),
    (re.compile(r"(?i)\bgh\s+(?:auth|api|secret|release|repo\s+create|repo\s+fork|pr\s+merge)\b"), "GitHub CLI high-risk operation is blocked"),

    (re.compile(r"(?i)\b(?:cat|less|more|head|tail|sed|awk|python|python3|node|ruby|perl|sqlite3)\b.*(?:\.env\b|\.env\.|\.pem\b|\.key\b|id_rsa|id_ed25519|credentials?|secrets?)"), "reading secret-looking files is blocked"),
    (re.compile(r"(?i)(api[_-]?key|secret|token|password)\s*[:=]\s*['\"]?[A-Za-z0-9_./+=:-]{20,}"), "inline secret-looking value in command is blocked"),
]

SCRIPT_EXTENSIONS = {
    ".sh", ".bash", ".zsh", ".fish",
    ".py", ".rb", ".pl", ".js", ".mjs", ".cjs", ".ts", ".php"
}

INTERPRETERS = {
    "bash", "sh", "zsh", "fish",
    "python", "python3", "ruby", "perl", "node", "deno", "php"
}

SCRIPT_FORBIDDEN_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (SEARCH_OR_DISCOVERY, "script contains shell search/file-discovery command"),
    (GLOBAL_CDIDX, "script contains global cdidx call"),
    (GIT_GREP, "script contains git grep"),
    *DANGEROUS_PATTERNS,
]

SECRET_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"AKIA[0-9A-Z]{16}"), "AWS access key"),
    (re.compile(r"ASIA[0-9A-Z]{16}"), "AWS temporary access key"),
    (re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----"), "private key"),
    (re.compile(r"ghp_[A-Za-z0-9_]{30,}"), "GitHub classic token"),
    (re.compile(r"github_pat_[A-Za-z0-9_]{60,}"), "GitHub fine-grained token"),
    (re.compile(r"sk-[A-Za-z0-9_-]{20,}"), "OpenAI-style API key"),
    (re.compile(r"xox[baprs]-[A-Za-z0-9-]{20,}"), "Slack token"),
    (re.compile(r"AIza[0-9A-Za-z_-]{35}"), "Google API key"),
    (re.compile(r"(?i)(api[_-]?key|secret|token|password)\s*[:=]\s*['\"]?[A-Za-z0-9_./+=:-]{20,}"), "generic secret assignment"),
]


def emit_deny(reason: str) -> None:
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }, ensure_ascii=False))
    sys.exit(0)


def load_payload() -> dict:
    try:
        return json.load(sys.stdin)
    except Exception as exc:
        emit_deny(f"failed to parse Claude Code hook input; failing closed: {exc}")


def get_command(payload: dict) -> str:
    tool_input = payload.get("tool_input") or {}
    command = tool_input.get("command")
    if not isinstance(command, str):
        emit_deny("Bash command missing from hook input; failing closed")
    return command


def is_relative_to(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


def is_safe_local_cdidx_command(command: str, project_root: Path) -> bool:
    """Allow only the locally built CodeIndex DLL, with no shell control operators."""
    if SHELL_CONTROL_RE.search(command):
        return False
    try:
        tokens = shlex.split(command, posix=True)
    except ValueError:
        return False
    if len(tokens) < 2:
        return False
    if tokens[0] != "dotnet":
        return False

    dll = Path(tokens[1])
    if not dll.is_absolute():
        dll = (project_root / dll).resolve()
    expected = (project_root / LOCAL_CDIDX_REL).resolve()
    return dll == expected


def resolve_candidate(token: str, cwd: Path) -> Path | None:
    if not token or token.startswith("-"):
        return None
    if token in {"-c", "-e", "--eval", "-"}:
        return None
    p = Path(token)
    if not p.is_absolute():
        p = cwd / p
    try:
        return p.resolve()
    except Exception:
        return None


def candidate_script_paths(command: str, cwd: Path) -> list[Path]:
    try:
        tokens = shlex.split(command, posix=True)
    except ValueError:
        return []

    if not tokens:
        return []

    result: list[Path] = []
    first = Path(tokens[0]).name

    if first in INTERPRETERS:
        if any(t in {"-c", "-e", "--eval"} for t in tokens[1:]):
            emit_deny("inline interpreter execution is blocked; use a reviewed script file instead")
        for token in tokens[1:]:
            if token.startswith("-"):
                continue
            path = resolve_candidate(token, cwd)
            if path is not None:
                result.append(path)
            break
        return result

    path = resolve_candidate(tokens[0], cwd)
    if path and (tokens[0].startswith("./") or path.suffix in SCRIPT_EXTENSIONS):
        result.append(path)

    return result


def check_raw_command(command: str, project_root: Path) -> None:
    if is_safe_local_cdidx_command(command, project_root):
        return

    # ===== exception =====
    # codex
    if re.search(r"\bnode\b.*codex-companion\.mjs", command):
        if re.search(r"(curl|rm\s|ssh|scp|wget|nc|bash\s+-c)", command):
            emit_deny("codex plugin tried dangerous command")
        return

    # dotnet test
    if re.match(r"^\s*dotnet\s+test\b", command):
        return

    # dotnet run
    if re.match(r"^\s*(cd\s+\S+\s*&&\s*)?dotnet\s+run\b", command):
        if re.search(r"\b(rm|curl|ssh|scp|wget)\b", command):
            emit_deny("dangerous command chained to dotnet run")
        return

    # xargs
    if re.search(r"\bxargs\b.*codex\s+exec\b", command):
        if re.search(r"(rm|curl|ssh|scp|wget|nc|bash\s+-c)", command):
            emit_deny("dangerous xargs usage is not allowed")
        return

    # ===== global dangerous syntax =====
    if re.search(r"\$\(", command):
        emit_deny("command substitution is not allowed")

    # ===== /tmp read-only commands =====
    if "/tmp/" in command and re.match(r"^\s*(ls|cat|wc|tail|head|awk|sed)\b", command):
        return

    # ===== safe shortcuts =====
    if re.match(r"^\s*(ls|cat|wc|tail|head)\b", command) and "/tmp/" in command:
        return

    # ===== awk =====
    if re.search(r"\bawk\b.*system\s*\(", command):
        emit_deny("awk system() is not allowed")

    if re.match(r"^\s*awk\b", command):
        return

    # ===== sed =====
    if re.search(r"\bsed\b.*\be\b", command):
        emit_deny("sed execution is not allowed")

    if re.match(r"^\s*sed\b", command):
        return

    # ===== perl =====
    if re.match(r"^\s*perl\b", command):
        emit_deny("perl execution is disabled")

    # ===== python =====
    if re.match(r"^\s*python3?\b", command):
        emit_deny("python execution is disabled")

    # ===== kill =====
    if re.search(r"\bkill\b.*\$\(", command):
        emit_deny("kill with command substitution is not allowed")

    if re.search(r"\bpgrep\b", command) and "kill" in command:
        emit_deny("pgrep + kill combination is not allowed")

    # ===== git =====
    if re.match(r"^\s*git\s+commit\b", command):
        if re.search(r"\$\(", command):
            if not re.search(r"\$\(\s*cat\s+<<", command):
                emit_deny("unsafe command substitution in git commit")
        return

    if re.match(r"^\s*git\s+add\b", command):
        if re.search(r"\b(\.|-A|--all)\b", command):
            emit_deny("bulk git add is not allowed")
        return

    if re.match(r"^\s*git\s+status\b", command):
        return

    # ===== fallback =====
    for pattern, reason in DANGEROUS_PATTERNS:
        if pattern.search(command):
            emit_deny(reason)


def check_script(path: Path, project_root: Path) -> None:
    if re.search(r"codex-companion\.mjs$", str(path)):
        return

    if not path.exists():
        return
    if not path.is_file():
        return

    if not is_relative_to(path, project_root):
        emit_deny(f"script outside project is blocked: {path}")

    try:
        data = path.read_bytes()[:MAX_SCRIPT_SCAN_BYTES]
    except Exception as exc:
        emit_deny(f"could not inspect script before execution; failing closed: {path}: {exc}")

    text = data.decode("utf-8", errors="ignore")
    for pattern, reason in SCRIPT_FORBIDDEN_PATTERNS:
        if pattern.search(text):
            emit_deny(f"{reason}: {path}")


def staged_secret_check(cwd: Path) -> None:
    gitleaks = shutil.which("gitleaks")
    if gitleaks:
        proc = subprocess.run(
            [gitleaks, "protect", "--staged", "--redact", "--verbose"],
            cwd=str(cwd),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=60,
        )
        if proc.returncode != 0:
            output = (proc.stdout or "").strip()
            if len(output) > 2000:
                output = output[:2000] + "\n..."
            emit_deny("gitleaks blocked this commit:\n" + output)
        return

    proc = subprocess.run(
        ["git", "diff", "--cached", "--unified=0", "--no-ext-diff"],
        cwd=str(cwd),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        timeout=30,
    )
    if proc.returncode != 0:
        emit_deny("could not inspect staged diff for secrets; install gitleaks or fix git diff")

    added_lines = "\n".join(
        line[1:] for line in proc.stdout.splitlines()
        if line.startswith("+") and not line.startswith("+++")
    )
    for pattern, name in SECRET_PATTERNS:
        if pattern.search(added_lines):
            emit_deny(f"secret-looking staged content detected before commit: {name}; install gitleaks for better scanning")


def main() -> None:
    payload = load_payload()
    command = get_command(payload)
    cwd = Path(payload.get("cwd") or os.getcwd()).resolve()
    project_root = Path(os.environ.get("CLAUDE_PROJECT_DIR") or cwd).resolve()

    check_raw_command(command, project_root)

    for script in candidate_script_paths(command, cwd):
        check_script(script, project_root)

    if re.search(r"(?i)(^|[\s;&|()`])git\s+commit\b", command):
        staged_secret_check(cwd)

    sys.exit(0)


if __name__ == "__main__":
    main()
