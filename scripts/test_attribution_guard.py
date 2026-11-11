#!/usr/bin/env python3
"""Tests for .claude/hooks/attribution_guard.py.

Feeds realistic PreToolUse hook JSON to the guard and asserts block / allow.
No SolidWorks needed:  py -3 scripts/test_attribution_guard.py
"""
import json
import subprocess
import sys
import tempfile
from pathlib import Path

GUARD = Path(__file__).resolve().parent.parent / ".claude" / "hooks" / "attribution_guard.py"

# Built from fragments so this file itself stays free of the literal banned text.
NAME = "Cl" + "aude"
COAUTHOR = "Co-Authored-" + "By: " + NAME + " Opus <noreply@" + "anthropic.com>"
SESSION = NAME + "-Session: https://" + "claude.ai/code/session_01ABC"
GENERATED = "\U0001F916 Generated " + "with [" + NAME + " Code](https://" + "claude.com/claude-code)"
URL = "https://" + "claude.ai/code/session_01ABC"

tmp = Path(tempfile.mkdtemp(prefix="attrib_guard_test_"))
BAD_FILE = tmp / "bad.md"
BAD_FILE.write_text("Fix the thing\n\n" + COAUTHOR + "\n", encoding="utf-8")
GOOD_FILE = tmp / "good.md"
GOOD_FILE.write_text("Fix the thing\n\nNothing special here.\n", encoding="utf-8")


def run_guard(tool, command, cwd=None):
    payload = {"hook_event_name": "PreToolUse", "tool_name": tool, "cwd": str(cwd or tmp),
               "tool_input": {"command": command}}
    out = subprocess.run([sys.executable, str(GUARD)], input=json.dumps(payload).encode("utf-8"),
                         capture_output=True, timeout=60)
    text = out.stdout.decode("utf-8", errors="replace").strip()
    return out.returncode == 0 and '"permissionDecision": "deny"' in text


def git_commit_heredoc(trailer):
    return ('git commit -m "$(cat <<\'EOF\'\nFix the thing\n\n' + trailer + '\nEOF\n)"')


def ps_here_string(trailer):
    return "git commit -m @'\nFix the thing\n\n" + trailer + "\n'@"


# (name, tool, command, expect_block)
CASES = [
    ("commit -m with trailer", "Bash", f'git commit -m "Fix\n\n{COAUTHOR}"', True),
    ("commit -m two -m flags", "Bash", f'git commit -m "Fix" -m "{COAUTHOR}"', True),
    ("commit heredoc trailer", "Bash", git_commit_heredoc(COAUTHOR), True),
    ("commit heredoc session line", "Bash", git_commit_heredoc(SESSION), True),
    ("commit with git -C and --amend", "Bash", f'git -C repo commit --amend -m "x\n{COAUTHOR}"', True),
    ("commit chained after add", "Bash", f'git add -A && git commit -m "x" -m "{COAUTHOR}"', True),
    ("commit in PowerShell here-string", "PowerShell", ps_here_string(COAUTHOR), True),
    ("commit PowerShell session line", "PowerShell", ps_here_string(SESSION), True),
    ("commit via call operator and git.exe", "PowerShell", f'& "C:\\Program Files\\Git\\cmd\\git.exe" commit -m "x {COAUTHOR}"', True),
    ("commit -F file with trailer", "Bash", f'git commit -F "{BAD_FILE}"', True),
    ("commit --file=path", "Bash", f'git commit --file="{BAD_FILE}"', True),
    ("commit -F via file substitution", "Bash", f'git commit -m "$(cat {BAD_FILE.as_posix()})"', True),
    ("PowerShell Get-Content substitution", "PowerShell", f'git commit -m (Get-Content -Raw "{BAD_FILE}")', True),
    ("gh pr create --body", "Bash", f'gh pr create --title t --body "Summary\n\n{GENERATED}"', True),
    ("gh pr create heredoc", "Bash", f"gh pr create --title t --body \"$(cat <<'EOF'\nSummary\n\n{GENERATED}\nEOF\n)\"", True),
    ("gh pr create --body-file", "Bash", f'gh pr create --title t --body-file "{BAD_FILE}"', True),
    ("gh pr comment session url", "Bash", f'gh pr comment 5 --body "see {URL}"', True),
    ("gh pr merge --body", "Bash", f'gh pr merge 5 --merge --body "{COAUTHOR}"', True),
    ("gh issue create -F file", "Bash", f'gh issue create --title t -F "{BAD_FILE}"', True),
    ("gh issue comment", "Bash", f'gh issue comment 7 --body "{SESSION}"', True),
    ("gh api PATCH body field", "Bash", f'gh api -X PATCH repos/o/r/pulls/3 -f body="{GENERATED}"', True),
    ("gh api -F body=@file", "Bash", f'gh api repos/o/r/issues/3/comments -F body=@"{BAD_FILE}"', True),
    ("bash -c wrapper", "Bash", f"bash -c 'git commit -m \"x {COAUTHOR}\"'", True),
    ("case-insensitive trailer", "Bash", 'git commit -m "x\n\nco-authored-by:   CLAUDE <a@b>"', True),
    ("git tag annotated message", "Bash", f'git tag -a v1 -m "{COAUTHOR}"', True),
    ("mcp github tool", "mcp__github__create_issue", None, True),
    # Must be allowed
    ("clean commit", "Bash", 'git commit -m "Fix the thing" -m "Co-Authored-By: Alice <a@example.com>"', False),
    ("clean commit with file", "Bash", f'git commit -F "{GOOD_FILE}"', False),
    ("clean heredoc commit", "Bash", git_commit_heredoc("Refs #5"), False),
    ("clean PowerShell commit", "PowerShell", ps_here_string("Refs #5"), False),
    ("clean gh pr create", "Bash", 'gh pr create --title t --body "Adds a guard"', False),
    ("clean gh pr create body-file", "Bash", f'gh pr create --title t --body-file "{GOOD_FILE}"', False),
    ("git log --grep", "Bash", f'git log --grep="{NAME}" --oneline', False),
    ("git log --grep trailer", "Bash", f'git log --all --grep "Co-Authored-By: {NAME}"', False),
    ("grep -r name", "Bash", f"grep -r {NAME} .", False),
    ("grep -rn url", "Bash", f'grep -rn "claude.ai/code/session" docs', False),
    ("rg trailer", "Bash", f'rg -i "Co-Authored-By: {NAME}" --glob "*.md"', False),
    ("PowerShell Select-String", "PowerShell", f'Select-String -Path *.md -Pattern "{NAME}-Session"', False),
    ("git log format with body", "Bash", "git log -5 --format=%B", False),
    ("gh pr view", "Bash", "gh pr view 5 --json body --jq .body", False),
    ("gh api read", "Bash", "gh api repos/o/r/pulls/5/comments --jq '.[].body'", False),
    ("echo mentioning commit", "Bash", f'echo "never put {COAUTHOR} in a git commit"', False),
    ("non-shell tool", "Edit", None, False),
]


def main():
    passed = 0
    failed = []
    for name, tool, command, expect in CASES:
        if command is None:
            payload = {"tool_name": tool, "cwd": str(tmp), "tool_input": {"body": COAUTHOR if expect else "ok"}}
            out = subprocess.run([sys.executable, str(GUARD)], input=json.dumps(payload).encode("utf-8"),
                                 capture_output=True, timeout=60)
            got = '"permissionDecision": "deny"' in out.stdout.decode("utf-8", errors="replace")
        else:
            got = run_guard(tool, command)
        if got == expect:
            passed += 1
        else:
            failed.append(f"{name}: expected {'block' if expect else 'allow'}, got {'block' if got else 'allow'}")

    # --amend / -C reuse the message of an existing commit: block when that message has attribution.
    repo = tmp / "repo"
    repo.mkdir()
    env_git = ["git", "-c", "user.name=t", "-c", "user.email=t@example.com"]
    subprocess.run(["git", "init", "-q", str(repo)], check=True)
    subprocess.run(env_git + ["commit", "-q", "--allow-empty", "-m", "base\n\n" + COAUTHOR], cwd=repo, check=True)
    total = len(CASES) + 2
    for name, cmd, expect in (("amend --no-edit reusing a bad message", "git commit --amend --no-edit", True),
                              ("commit -C HEAD reusing a bad message", "git commit -C HEAD", True)):
        got = run_guard("Bash", cmd, cwd=repo)
        if got == expect:
            passed += 1
        else:
            failed.append(f"{name}: expected block, got allow")

    for line in failed:
        print("FAIL", line)
    print(f"{passed}/{total} passed")
    return 0 if not failed else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    finally:
        import shutil
        shutil.rmtree(tmp, ignore_errors=True)
