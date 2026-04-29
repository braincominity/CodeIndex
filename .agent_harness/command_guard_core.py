"""Shared command guard policy for Claude Code and Codex.

This module keeps the command classification and denial policy in one place
so the Claude and Codex hook adapters stay thin.
"""

from __future__ import annotations

from dataclasses import dataclass
import os
import re
import shlex
import shutil
import subprocess
from pathlib import Path


MAX_SCRIPT_SCAN_BYTES = 512 * 1024
LOCAL_CDIDX_REL = Path("src/CodeIndex/bin/Debug/net8.0/cdidx.dll")

_SHELL_CONTROL_TOKENS = {"|", "||", "&", "&&", ";", "|&", "(", ")", "<", ">", "<<", ">>"}
_INLINE_INTERPRETER_FLAGS = {"-c", "-e", "--eval"}

SEARCH_OR_DISCOVERY_RE = re.compile(
    r"""(?ix)
    (^|[^\w./-])
    (
      grep|egrep|fgrep|zgrep|rgrep|
      rg|ripgrep|ag|ack|ack-grep|
      find|fd|fdfind|locate|mlocate|mdfind
    )
    (?=$|[^\w./-])
    """
)

GLOBAL_CDIDX_RE = re.compile(
    r"""(?ix)
    (^|[^\w./-])
    (
      cdidx
      |~/?\.local/bin/cdidx
      |\$HOME/\.local/bin/cdidx
    )
    (?=$|[^\w./-])
    """
)

GIT_GREP_RE = re.compile(r"(?i)(^|[^\w./-])git\s+grep\b")
LOCAL_CDIDX_DLL_RE = re.compile(r"(?i)cdidx\.dll")
INLINE_SECRET_RE = re.compile(r"(?i)(api[_-]?key|secret|token|password)\s*[:=]\s*['\"]?[A-Za-z0-9_./+=:-]{20,}")
COMMAND_SUBSTITUTION_RE = re.compile(r"`|\$\(")
CONTROL_OP_RE = re.compile(r"(?s)(?:&&|\|\||;|\|&|\||&|`|\$\(|<|>|\n)")
INLINE_INTERPRETER_RE = re.compile(r"(?i)^\s*(?:python|python3|ruby|perl|node)\b.*(?:\s(?:-c|-e|--eval)\b)")

_FORBIDDEN_COMMAND_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (SEARCH_OR_DISCOVERY_RE, "shell search/file-discovery command is blocked; use dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll instead."),
    (GLOBAL_CDIDX_RE, "global cdidx is blocked; use dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll instead."),
    (GIT_GREP_RE, "git grep is blocked; use dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll instead."),
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
    (INLINE_SECRET_RE, "inline secret-looking value in command is blocked"),
]

_SCRIPT_FORBIDDEN_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (SEARCH_OR_DISCOVERY_RE, "script contains shell search/file-discovery command"),
    (GIT_GREP_RE, "script contains git grep"),
    *_FORBIDDEN_COMMAND_PATTERNS,
]

_SECRET_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"AKIA[0-9A-Z]{16}"), "AWS access key"),
    (re.compile(r"ASIA[0-9A-Z]{16}"), "AWS temporary access key"),
    (re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----"), "private key"),
    (re.compile(r"ghp_[A-Za-z0-9_]{30,}"), "GitHub classic token"),
    (re.compile(r"github_pat_[A-Za-z0-9_]{60,}"), "GitHub fine-grained token"),
    (re.compile(r"sk-[A-Za-z0-9_-]{20,}"), "OpenAI-style API key"),
    (re.compile(r"xox[baprs]-[A-Za-z0-9-]{20,}"), "Slack token"),
    (re.compile(r"AIza[0-9A-Za-z_-]{35}"), "Google API key"),
    (INLINE_SECRET_RE, "generic secret assignment"),
]


@dataclass(frozen=True)
class GuardDecision:
    allowed: bool
    reason: str


def _allow(reason: str) -> GuardDecision:
    return GuardDecision(True, reason)


def _deny(reason: str) -> GuardDecision:
    return GuardDecision(False, reason)


def _split_command(command: str) -> list[str]:
    try:
        return shlex.split(command, posix=True)
    except ValueError:
        return []


def _token_path(token: str, cwd: Path) -> Path | None:
    if not token or token.startswith("-"):
        return None
    if token in {"-c", "-e", "--eval", "-", "source", "."}:
        return None
    expanded = Path(os.path.expandvars(os.path.expanduser(token)))
    if not expanded.is_absolute():
        expanded = cwd / expanded
    try:
        return expanded.resolve()
    except Exception:
        return None


def _command_mentions_local_cdidx(command: str, project_root: Path) -> bool:
    tokens = _split_command(command)
    if len(tokens) < 2:
        return False
    if tokens[0] != "dotnet":
        return False
    dll = _token_path(tokens[1], project_root)
    expected = (project_root / LOCAL_CDIDX_REL).resolve()
    return dll == expected


def _command_is_safe_local_cdidx(command: str, cwd: Path, project_root: Path) -> bool:
    if CONTROL_OP_RE.search(command):
        return False
    if COMMAND_SUBSTITUTION_RE.search(command):
        return False
    tokens = _split_command(command)
    if len(tokens) < 2:
        return False
    if tokens[0] != "dotnet":
        return False
    dll = _token_path(tokens[1], cwd)
    expected = (project_root / LOCAL_CDIDX_REL).resolve()
    return dll == expected


def _contains_inline_interpreter(command: str) -> bool:
    return bool(INLINE_INTERPRETER_RE.search(command))


def _contains_secret_like_assignment(text: str) -> bool:
    return bool(INLINE_SECRET_RE.search(text))


def evaluate_bash_command(command: str, cwd: Path, project_root: Path) -> GuardDecision:
    if not isinstance(command, str) or not command.strip():
        return _deny("Bash command missing from hook input; failing closed")

    if _command_is_safe_local_cdidx(command, cwd, project_root):
        return _allow("local cdidx command")

    if _command_mentions_local_cdidx(command, project_root):
        return _deny("local cdidx commands must not use shell control operators or command substitutions")

    if LOCAL_CDIDX_DLL_RE.search(command):
        return _deny("use dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll instead")

    for pattern, reason in _FORBIDDEN_COMMAND_PATTERNS:
        if pattern.search(command):
            return _deny(reason)

    if _contains_inline_interpreter(command):
        return _deny("inline interpreter execution is blocked; use a reviewed script file instead")

    return _allow("bash-guard allow")


def candidate_script_paths(command: str, cwd: Path) -> list[Path]:
    tokens = _split_command(command)
    if not tokens:
        return []

    result: list[Path] = []
    first = Path(tokens[0]).name

    if first in {"source", "."} and len(tokens) > 1:
        path = _token_path(tokens[1], cwd)
        if path is not None:
            result.append(path)
        return result

    if first in {"bash", "sh", "zsh", "fish", "python", "python3", "ruby", "perl", "node", "deno", "php"}:
        if any(token in _INLINE_INTERPRETER_FLAGS for token in tokens[1:]):
            return result
        for token in tokens[1:]:
            if token.startswith("-"):
                continue
            path = _token_path(token, cwd)
            if path is not None:
                result.append(path)
            break
        return result

    path = _token_path(tokens[0], cwd)
    if path is None:
        return result

    if tokens[0].startswith(("./", "../", "/", "~")) or path.suffix.lower() in {
        ".sh", ".bash", ".zsh", ".fish", ".py", ".rb", ".pl", ".js", ".mjs", ".cjs", ".ts", ".php"
    }:
        result.append(path)

    return result


def _is_relative_to(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


def check_script_file(path: Path, project_root: Path) -> GuardDecision:
    if not path.exists():
        return _allow(f"script not found: {path}")
    if not path.is_file():
        return _deny(f"candidate script is not a file: {path}")

    project_root = project_root.resolve()
    try:
        resolved = path.resolve()
    except Exception as exc:
        return _deny(f"could not resolve script path; failing closed: {path}: {exc}")

    if not _is_relative_to(resolved, project_root):
        return _deny(f"script outside project is blocked: {path}")

    try:
        data = resolved.read_bytes()[:MAX_SCRIPT_SCAN_BYTES]
    except Exception as exc:
        return _deny(f"could not inspect script before execution; failing closed: {path}: {exc}")

    text = data.decode("utf-8", errors="ignore")
    for pattern, reason in _SCRIPT_FORBIDDEN_PATTERNS:
        if pattern.search(text):
            return _deny(f"{reason}: {path}")
    return _allow(f"script allowed: {path}")


def staged_secret_check(cwd: Path) -> GuardDecision:
    gitleaks = shutil.which("gitleaks")
    if gitleaks:
        try:
            proc = subprocess.run(
                [gitleaks, "protect", "--staged", "--redact", "--verbose"],
                cwd=str(cwd),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                timeout=60,
                check=False,
            )
        except Exception as exc:
            return _deny(f"could not inspect staged diff for secrets with gitleaks; failing closed: {exc}")
        if proc.returncode != 0:
            output = (proc.stdout or "").strip()
            if len(output) > 2000:
                output = output[:2000] + "\n..."
            return _deny("gitleaks blocked this commit:\n" + output)
        return _allow("gitleaks passed")

    try:
        proc = subprocess.run(
            ["git", "diff", "--cached", "--unified=0", "--no-ext-diff"],
            cwd=str(cwd),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=30,
            check=False,
        )
    except Exception as exc:
        return _deny(f"could not inspect staged diff for secrets; failing closed: {exc}")
    if proc.returncode != 0:
        return _deny("could not inspect staged diff for secrets; install gitleaks or fix git diff")

    added_lines = "\n".join(
        line[1:]
        for line in (proc.stdout or "").splitlines()
        if line.startswith("+") and not line.startswith("+++")
    )
    for pattern, name in _SECRET_PATTERNS:
        if pattern.search(added_lines):
            return _deny(
                f"secret-looking staged content detected before commit: {name}; install gitleaks for better scanning"
            )
    return _allow("staged secret scan passed")
