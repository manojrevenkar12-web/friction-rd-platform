"""Client for rdo-core, the platform's C# decision code (planner, intake validator, evaluators)
behind a JSON-lines protocol. The lab measures the code the service runs, not a Python copy."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path
from typing import Any

from dynolab.paths import ROOT


def core_command() -> list[str]:
    exe = os.environ.get("RDO_CORE")
    if exe:
        return [exe]
    built = ROOT / "src" / "Rdo.Cli" / "bin" / "Release" / "net8.0" / "rdo-core.dll"
    if not built.exists():
        raise FileNotFoundError(f"{built} not found: run `dotnet build src/Rdo.Cli -c Release` first")
    return [shutil.which("dotnet") or "dotnet", str(built)]


class Core:
    def __init__(self, master_path: Path, settings_path: Path | None = None) -> None:
        settings_path = settings_path or ROOT / "config" / "platform.json"
        self.proc = subprocess.Popen([*core_command(), str(master_path), str(settings_path)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                     text=True, bufsize=1)
        assert self.call({"cmd": "ping"}) == {"ok": True}

    def call(self, request: dict[str, Any]) -> Any:
        assert self.proc.stdin and self.proc.stdout
        self.proc.stdin.write(json.dumps(request, ensure_ascii=False) + "\n")
        self.proc.stdin.flush()
        out = json.loads(self.proc.stdout.readline())
        if isinstance(out, dict) and "error" in out:
            raise RuntimeError(f"rdo-core: {out['error']} for {request.get('cmd')}")
        return out

    def close(self) -> None:
        if self.proc.stdin:
            self.proc.stdin.close()
        self.proc.wait(timeout=10)

    def __enter__(self) -> Core:
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()
