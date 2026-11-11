#!/usr/bin/env python3
"""PreToolUse hook: refuse tool calls that would write AI-assistant attribution
into git history, pull requests, issues or comments.

Blocked content (case-insensitive): a "Co-Authored-By" trailer naming Claude,
"Claude-Session:" lines, "Generated with [Claude Code]" (and the robot-emoji
variant), claude.ai session URLs, and the Anthropic noreply address.

Only commands that WRITE text are inspected (git commit/merge/tag/notes/revert/
stash, gh pr/issue/release write subcommands, gh api calls with a write method
or body fields), so reading commands such as `git log --grep` or `grep -r` are
never blocked. For a writing command the whole command text is checked (this
covers -m, heredocs, PowerShell here-strings and stdin), and so is the content
of files passed through -F / --file / --body-file / --message-file /
--template / --input, `key=@file`, and `cat` / `Get-Content` substitutions.
`git commit --amend` / `-C` / `-c` / `--reuse-message` also check the message
of the commit that would be reused.

Matched tools (see .claude/settings.json): Bash, PowerShell, GitHub MCP tools.
Input: hook JSON on stdin. Output: a JSON "deny" decision, or nothing to allow.
"""
import json
import os
import re
import subprocess
import sys
from pathlib import Path

BANNED = re.compile(
    r"Co-Authored-By:\s*Claude"
    r"|Claude-Session\s*:"
    r"|Generated\s+with\s+\[?Claude"
    "|\U0001F916\\s*Generated"
    r"|claude\.ai/(?:code/)?session"
    r"|noreply@anthropic\.com",
    re.IGNORECASE,
)

ARG = r"(?:\"[^\"]*\"|'[^']*'|\S+)"
_EXE = r"(?:(?:\"[^\"]*|'[^']*|\S*)[\\/])?"
_GIT = _EXE + r"git(?:\.exe)?[\"']?(?:\s+(?:-C\s+" + ARG + r"|-c\s+" + ARG + r"|--[\w-]+(?:=\S+)?))*\s+"
_GH = _EXE + r"gh(?:\.exe)?[\"']?\s+"
PREFIX = (
    r"^[\s(){}!&]*"
    r"(?:(?:if|elif|else|then|do|try|foreach|for|while|until|time|sudo|command|exec|env|call)\b[^;&|]*?[\s{(])?"
    r"[\s(){}!&]*(?:\w+=\S*\s+)*"
)
WRITES = re.compile(
    PREFIX + "(?:"
    + _GIT + r"(?:commit|merge|tag|notes|revert|stash)\b"
    + "|" + _GH + r"(?:pr\s+(?:create|edit|comment|review|merge|close|reopen)"
    r"|issue\s+(?:create|edit|comment|close|reopen)"
    r"|release\s+(?:create|edit))\b"
    + "|" + _GH + r"api\b.*(?:(?:-X|--method)[\s=]*['\"]?(?:POST|PATCH|PUT|DELETE)"
    r"|\s-[fF]\s*\S|--field|--raw-field|--input)"
    ")",
    re.IGNORECASE,
)
# A wrapper can hide the real command inside a quoted string.
WRAPPER = re.compile(
    r"^[\s(){}!&]*(?:\w+=\S*\s+)*(?:\S*[\\/])?(?:bash|sh|zsh|dash|pwsh|powershell|cmd|wsl|eval|xargs|iex|"
    r"invoke-expression|start-process|start)(?:\.exe)?\b",
    re.IGNORECASE,
)
WRITES_ANYWHERE = re.compile(
    _GIT.replace(r"^", "") + r"(?:commit|merge|tag|notes|revert|stash)\b"
    r"|" + _GH + r"(?:pr|issue|release|api)\b",
    re.IGNORECASE,
)
SPLIT = re.compile(r"&&|\|\||;|\||\r|\n")
FILE_OPT = re.compile(
    r"(?:^|\s)(?:-F|--file|--body-file|--message-file|--template|--input|-t)(?:\s*=\s*|\s+|(?=\S))(" + ARG + ")"
)
FIELD_FILE = re.compile(r"(?:^|[\s=])@(" + ARG + ")")
CAT_FILE = re.compile(
    r"(?:\bcat|\bGet-Content|\bgc|\btype)\s+(?:-(?:Raw|Path|LiteralPath)\s+)*(" + ARG + ")",
    re.IGNORECASE,
)
REUSE_OPT = re.compile(r"(?:^|\s)(?:--amend|-C|-c|--reuse-message|--reedit-message|--fixup)\b")
REUSE_REF = re.compile(r"(?:^|\s)(?:-C|-c|--reuse-message|--reedit-message)(?:\s*=\s*|\s+)(\S+)")


def unquote(token: str) -> str:
    token = token.strip()
    if len(token) >= 2 and token[0] == token[-1] and token[0] in "\"'":
        token = token[1:-1]
    return token


def to_path(raw: str, cwd: Path) -> Path | None:
    raw = unquote(raw)
    if not raw or raw == "-" or "$" in raw or "`" in raw:
        return None
    if raw.startswith("~"):
        raw = os.path.expanduser(raw)
    elif raw.startswith("/") and os.name == "nt":
        try:  # Git Bash style path such as /tmp/x or /c/Users/x
            out = subprocess.run(["cygpath", "-w", raw], capture_output=True, text=True, timeout=5)
            if out.returncode == 0 and out.stdout.strip():
                raw = out.stdout.strip()
        except (OSError, subprocess.SubprocessError):
            pass
    p = Path(raw)
    return p if p.is_absolute() else cwd / p


def referenced_files(command: str, cwd: Path) -> list[Path]:
    raws = [m.group(1) for m in FILE_OPT.finditer(command)]
    raws += [m.group(1) for m in CAT_FILE.finditer(command)]
    out = []
    for raw in raws:
        raw = unquote(raw)
        raw = raw.rstrip(")\"';`")
        if "=@" in raw:  # gh api -F body=@file
            raw = raw.split("=@", 1)[1]
        elif raw.startswith("@"):
            raw = raw[1:]
        p = to_path(raw, cwd)
        if p is not None:
            out.append(p)
    out += [p for p in (to_path(m.group(1), cwd) for m in FIELD_FILE.finditer(command)) if p is not None]
    return out


def reused_messages(command: str, cwd: Path) -> list[tuple[str, str]]:
    """Messages of existing commits that this git commit would carry over."""
    refs = []
    if re.search(r"(?:^|\s)--amend\b", command):
        refs.append("HEAD")
    refs += [unquote(m.group(1)) for m in REUSE_REF.finditer(command)]
    found = []
    for ref in refs:
        if not re.fullmatch(r"[\w./^~@{}-]+", ref):
            continue
        try:
            out = subprocess.run(["git", "log", "-1", "--format=%B", ref, "--"], cwd=str(cwd),
                                 capture_output=True, text=True, encoding="utf-8", errors="ignore", timeout=10)
        except (OSError, subprocess.SubprocessError):
            continue
        if out.returncode == 0:
            found.append((f"message of commit {ref}", out.stdout))
    return found


def shell_hits(command: str, cwd: Path) -> list[str]:
    segments = SPLIT.split(command)
    is_write = any(WRITES.search(s) for s in segments)
    if not is_write and any(WRAPPER.search(s) for s in segments):
        is_write = bool(WRITES_ANYWHERE.search(command))
    if not is_write:
        return []
    hits = []
    if BANNED.search(command):
        hits.append("the command text")
    for path in referenced_files(command, cwd):
        try:
            if BANNED.search(path.read_text(encoding="utf-8", errors="ignore")):
                hits.append(f"file {path.name}")
        except OSError:
            pass
    if re.search(r"git(?:\.exe)?\b.*\bcommit\b", command, re.IGNORECASE):
        for label, text in reused_messages(command, cwd):
            if BANNED.search(text):
                hits.append(label)
    return hits


def main() -> int:
    try:
        raw = sys.stdin.buffer.read().decode("utf-8", errors="replace")
        data = json.loads(raw)
    except Exception:
        return 0
    tool = data.get("tool_name") or ""
    tool_input = data.get("tool_input") or {}
    cwd = Path(data.get("cwd") or os.getcwd())
    hits: list[str] = []
    if tool in ("Bash", "PowerShell"):
        hits = shell_hits(tool_input.get("command") or "", cwd)
    elif tool.startswith("mcp__") and "github" in tool.lower():
        if BANNED.search(json.dumps(tool_input, ensure_ascii=False)):
            hits = ["the tool input"]
    if not hits:
        return 0
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": (
            "Blocked: " + ", ".join(hits) + " contains AI attribution or a session link (a Co-Authored-By "
            "trailer naming the assistant, a Claude-Session line, a 'Generated with' line, or a claude.ai "
            "session URL). Commits, pull requests, issues and comments must carry only the author's own "
            "information. Remove those lines and run the command again."
        ),
    }}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
