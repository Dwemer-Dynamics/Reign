"""Publish an opt-in Desktop HTML reader and ZIP; no network or runtime actions."""
from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import re
import runpy
import tempfile
import zipfile

HERE = Path(__file__).resolve().parent
CONFIG = Path(os.environ.get("USERPROFILE", str(Path.home()))) / ".reign/user-guide-publication.json"
SCHEMA = "reign-guide-desktop-publication-v1"
FILENAME = "Reign User Guide.html"
ZIP_FILENAME = "Reign User Guide.zip"

# Decode only the clicked image. Blob links preserve full-size picture viewing
# without embedding a second copy of every large PNG in the HTML.
PICTURE_LINKS = """<script>
document.querySelectorAll('a[data-embedded-image]').forEach(function(link) {
  function prepare() {
    if (link.href.startsWith('blob:')) return;
    var parts = link.querySelector('img').src.split(',');
    var binary = atob(parts[1]), bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    link.href = URL.createObjectURL(new Blob([bytes], {
      type: parts[0].slice(5, parts[0].indexOf(';'))
    }));
  }
  link.addEventListener('click', prepare);
  link.addEventListener('contextmenu', prepare);
});
</script>"""


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def atomic_write(path: Path, data: bytes) -> None:
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".reign-guide-", delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(data)
    try:
        assert sha(temporary.read_bytes()) == sha(data)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def publish_if_configured() -> dict | None:
    if not CONFIG.is_file():
        return None
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    assert config["schema"] == SCHEMA
    if Path(config["sourceDirectory"]).resolve() != HERE:
        return None
    desktop = Path(config["desktopDirectory"]).resolve(strict=True)
    target = desktop / FILENAME
    assert not target.is_symlink() and target.resolve().parent == desktop
    zip_target = desktop / ZIP_FILENAME
    assert not zip_target.is_symlink() and zip_target.resolve().parent == desktop

    # Refuse to replace the shared reader with a broken guide.
    runpy.run_path(str(HERE / "verify.py"))["main"]()
    content = (HERE / "index.html").read_text(encoding="utf-8")
    coverage = json.loads((HERE / "coverage.json").read_text(encoding="utf-8"))
    expected = {r["file"]: r["sha256"] for r in coverage["illustrations"]}
    embedded = {}

    def embed(match: re.Match) -> str:
        name = match[2]
        source = (HERE / name).resolve()
        assert source.is_relative_to((HERE / "assets").resolve())
        data = source.read_bytes()
        assert name in expected and sha(data) == expected[name]
        mime = "image/svg+xml" if source.suffix == ".svg" else "image/png"
        encoded = base64.b64encode(data).decode("ascii")
        assert sha(base64.b64decode(encoded, validate=True)) == expected[name]
        embedded[name] = expected[name]
        return match[1] + "data:" + mime + ";base64," + encoded + match[3]

    content = re.sub(r'(<img\b[^>]*\bsrc=")(assets/[^\"]+)(")', embed, content)
    assert embedded == expected, "Standalone reader is missing pictures"
    content, links = re.subn(r'href="assets/[^\"]+"', 'href="#" data-embedded-image="true"', content)
    assert links == len(expected)
    assert not re.search(r'(?:src|href)="assets/', content)
    content = content.replace("img-src 'self' data: file:", "img-src 'self' data: blob:")
    assert content.count("</body>") == 1
    content = content.replace("</body>", PICTURE_LINKS + "\n</body>")
    data = content.encode("utf-8")
    atomic_write(target, data)
    assert sha(target.read_bytes()) == sha(data)
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr(FILENAME, data)
    zip_data = buffer.getvalue()
    atomic_write(zip_target, zip_data)
    with zipfile.ZipFile(zip_target) as archive:
        assert archive.namelist() == [FILENAME]
        assert sha(archive.read(FILENAME)) == sha(target.read_bytes())
    return {"file": str(target), "bytes": len(data), "sha256": sha(data), "embeddedPictures": len(embedded),
            "zip": str(zip_target), "zipBytes": len(zip_data), "zipSha256": sha(zip_data)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configure-desktop", type=Path)
    args = parser.parse_args()
    if args.configure_desktop:
        desktop = args.configure_desktop.resolve(strict=True)
        target = desktop / FILENAME
        previous = json.loads(CONFIG.read_text(encoding="utf-8")) if CONFIG.exists() else {}
        owned = previous.get("schema") == SCHEMA and previous.get("desktopDirectory") == str(desktop)
        if (target.exists() or (desktop / ZIP_FILENAME).exists()) and not owned:
            raise RuntimeError("An unrelated file already uses the Desktop guide name; preserve it before configuring publication.")
        CONFIG.parent.mkdir(exist_ok=True)
        atomic_write(CONFIG, (json.dumps({"schema": SCHEMA, "sourceDirectory": str(HERE), "desktopDirectory": str(desktop)}, indent=2) + "\n").encode())
    result = publish_if_configured()
    print(json.dumps({"desktopPublication": result}))


if __name__ == "__main__":
    main()
