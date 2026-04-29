from __future__ import annotations

import importlib.util
import tempfile
import sys
from pathlib import Path
from unittest import TestCase
from unittest.mock import Mock, patch


def load_core():
    root = Path(__file__).resolve().parents[2]
    path = root / ".agent_harness" / "command_guard_core.py"
    spec = importlib.util.spec_from_file_location("command_guard_core", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"could not load guard core from {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


core = load_core()


class CommandGuardCoreTests(TestCase):
    def test_allows_local_cdidx_relative_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            expected = root / core.LOCAL_CDIDX_REL
            expected.parent.mkdir(parents=True, exist_ok=True)
            expected.write_text("", encoding="utf-8")

            decision = core.evaluate_bash_command(
                "dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll search SymbolExtractor",
                cwd=root,
                project_root=root,
            )

            self.assertTrue(decision.allowed)

    def test_allows_local_cdidx_absolute_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            expected = root / core.LOCAL_CDIDX_REL
            expected.parent.mkdir(parents=True, exist_ok=True)
            expected.write_text("", encoding="utf-8")

            decision = core.evaluate_bash_command(
                f"dotnet {expected.resolve()} symbols --lang csharp",
                cwd=root,
                project_root=root,
            )

            self.assertTrue(decision.allowed)

    def test_denies_global_cdidx_and_search_tools(self) -> None:
        root = Path("/tmp")
        for command in (
            "cdidx search SymbolExtractor",
            "~/.local/bin/cdidx search SymbolExtractor",
            "$HOME/.local/bin/cdidx search SymbolExtractor",
            "grep -R SymbolExtractor src",
            "git grep SymbolExtractor",
            "find . -name '*.cs'",
        ):
            with self.subTest(command=command):
                decision = core.evaluate_bash_command(command, cwd=root, project_root=root)
                self.assertFalse(decision.allowed)

    def test_denies_chained_local_cdidx(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            expected = root / core.LOCAL_CDIDX_REL
            expected.parent.mkdir(parents=True, exist_ok=True)
            expected.write_text("", encoding="utf-8")

            decision = core.evaluate_bash_command(
                "dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll search Foo | cat",
                cwd=root,
                project_root=root,
            )

            self.assertFalse(decision.allowed)

    def test_denies_inline_interpreter_execution(self) -> None:
        root = Path("/tmp")
        decision = core.evaluate_bash_command("python3 -c 'print(1)'", cwd=root, project_root=root)
        self.assertFalse(decision.allowed)

    def test_candidate_script_paths_detects_direct_and_interpreter_scripts(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            script = root / "tools" / "guard.py"
            script.parent.mkdir(parents=True, exist_ok=True)
            script.write_text("print('ok')", encoding="utf-8")

            direct = core.candidate_script_paths("./tools/guard.py", cwd=root)
            interpreter = core.candidate_script_paths("python3 tools/guard.py", cwd=root)
            sourced = core.candidate_script_paths("source tools/guard.py", cwd=root)

            self.assertEqual([script.resolve()], direct)
            self.assertEqual([script.resolve()], interpreter)
            self.assertEqual([script.resolve()], sourced)

    def test_check_script_file_denies_outside_project_root(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            outside = Path(tmp).parent / "guard.py"
            outside.write_text("print('ok')", encoding="utf-8")

            decision = core.check_script_file(outside, project_root=root)

            self.assertFalse(decision.allowed)

    def test_check_script_file_denies_forbidden_content(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            script = root / "tools" / "guard.sh"
            script.parent.mkdir(parents=True, exist_ok=True)
            script.write_text("grep SymbolExtractor src\n", encoding="utf-8")

            decision = core.check_script_file(script, project_root=root)

            self.assertFalse(decision.allowed)

    def test_staged_secret_check_uses_git_diff_fallback(self) -> None:
        fake_proc = Mock(returncode=0, stdout="+ api_key = 'sk-abcdefghijklmnopqrstuvwx123456'\n", stderr="")

        with patch.object(core.shutil, "which", return_value=None), patch.object(core.subprocess, "run", return_value=fake_proc):
            decision = core.staged_secret_check(Path("/tmp"))

        self.assertFalse(decision.allowed)
