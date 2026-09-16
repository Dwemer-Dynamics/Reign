"""Generate or verify the tracked shared portrait source inventory."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path

TICKS_EPOCH = 621355968000000000
ALLOWED = {
    "portrait.png", "portrait_chest.png", "thumbnail_wide.png", "thumbnail.png", "zoom.png",
    "portrait_input.json", ".portrait_derivatives.json", ".ai_generation.json", "formal_outfit.json", "prompt.txt",
}


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def build(root: Path) -> dict:
    files = []
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Shared portrait source contains a link: {path}")
        if not path.is_file():
            continue
        relative = path.relative_to(root)
        if len(relative.parts) != 2 or relative.name not in ALLOWED:
            raise ValueError(f"Shared portrait source contains an unsupported path: {relative.as_posix()}")
        files.append({
            "path": relative.as_posix(),
            "bytes": path.stat().st_size,
            "sha256": digest(path),
            "lastWriteUtcTicks": path.stat().st_mtime_ns // 100 + TICKS_EPOCH,
        })
    return {
        "schema": "reign-shared-content-inventory-v1",
        "root": "_shared",
        "files": files,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--root", type=Path)
    parser.add_argument("--inventory", type=Path)
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[1]
    root = (args.root or repository / "ReignBeta" / "PortraitCache" / "_shared").resolve()
    inventory = (args.inventory or repository / "ReignBeta" / "PortraitCache" / "shared-portrait-inventory.json").resolve()
    expected_root = (repository / "ReignBeta" / "PortraitCache" / "_shared").resolve()
    expected_inventory = (repository / "ReignBeta" / "PortraitCache" / "shared-portrait-inventory.json").resolve()
    if root != expected_root or inventory != expected_inventory:
        raise ValueError("The authoritative shared portrait source paths cannot be redirected")
    value = build(root)
    encoded = (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    if args.check:
        if not inventory.is_file() or inventory.read_bytes() != encoded:
            raise SystemExit("Shared portrait inventory differs from authoritative source")
        print(json.dumps({"ok": True, "files": len(value["files"]), "root": str(root)}))
        return
    inventory.parent.mkdir(parents=True, exist_ok=True)
    temporary = inventory.with_name(inventory.name + ".new")
    temporary.write_bytes(encoded)
    os.replace(temporary, inventory)
    print(json.dumps({"ok": True, "files": len(value["files"]), "inventory": str(inventory)}))


if __name__ == "__main__":
    main()
