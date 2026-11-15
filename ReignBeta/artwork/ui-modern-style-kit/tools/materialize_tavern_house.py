"""Compatibility entry point for the approved native-coordinate Tavern artwork pipeline."""
from pathlib import Path
import shutil
import subprocess

if __name__ == "__main__":
    shell = shutil.which("pwsh")
    if not shell:
        raise SystemExit("The Tavern artwork materializer requires PowerShell 7.4 or newer and Windows System.Drawing.")
    raise SystemExit(subprocess.call([
        shell, "-NoProfile", "-File", str(Path(__file__).with_suffix(".ps1"))
    ]))
