"""Build the offline manual using only Python's standard library.

manual.md is the prose source. No game, server, provider, network or build tools
are invoked. Generated coverage records are an editorial inventory, not native
feature certification. Run from any directory: python docs/user-guide/build.py
"""
from __future__ import annotations

from datetime import date
import hashlib
import html
import json
import re
import runpy
import shutil
import struct
import textwrap
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def image_dimensions(path: Path) -> tuple[int, int]:
    if path.suffix == ".svg":
        box = ET.parse(path).getroot().attrib["viewBox"].split()
        return int(float(box[2])), int(float(box[3]))
    with path.open("rb") as stream:
        header = stream.read(24)
    assert header[:8] == b"\x89PNG\r\n\x1a\n" and header[12:16] == b"IHDR", path
    return struct.unpack("!II", header[16:24])


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", text.lower().replace("'", "").replace("’", "")).strip("-")


def inline(text: str) -> str:
    text = html.escape(text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"`([^`]+)`", r"<code>\1</code>", text)
    return text


def render_markdown(lines: list[str]) -> str:
    out: list[str] = []
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        if not line:
            i += 1
            continue
        image = re.fullmatch(r"!\[(.*?)\]\((.*?)\)", line)
        if image:
            alt, path = image.groups()
            width, height = image_dimensions(HERE / path)
            out.append(f'<figure><a href="{html.escape(path)}" target="_blank" rel="noopener" aria-label="Open larger: {html.escape(alt)}"><img src="{html.escape(path)}" alt="{html.escape(alt)}" loading="lazy" width="{width}" height="{height}"></a><figcaption>{inline(alt)}</figcaption></figure>')
            i += 1
            continue
        if line.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                cells = [s.strip() for s in lines[i].strip().strip("|").split("|")]
                if not all(re.fullmatch(r"[: -]+", c) for c in cells):
                    rows.append(cells)
                i += 1
            head = "".join(f'<th scope="col">{inline(c)}</th>' for c in rows[0])
            body = "".join("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in row) + "</tr>" for row in rows[1:])
            out.append(f'<div class="table-wrap" role="region" aria-label="Reference table" tabindex="0"><table><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table></div>')
            continue
        if line.startswith("> "):
            notes = []
            while i < len(lines) and lines[i].strip().startswith("> "):
                notes.append(lines[i].strip()[2:])
                i += 1
            kind = "preview" if notes[0].startswith("Preview:") else "note"
            out.append(f'<aside class="callout {kind}"><p>{inline(" ".join(notes))}</p></aside>')
            continue
        if re.match(r"^\d+\. ", line):
            items = []
            while i < len(lines) and re.match(r"^\d+\. ", lines[i].strip()):
                items.append(re.sub(r"^\d+\. ", "", lines[i].strip()))
                i += 1
            out.append("<ol>" + "".join(f"<li>{inline(s)}</li>" for s in items) + "</ol>")
            continue
        para = [line]
        i += 1
        while i < len(lines) and lines[i].strip():
            para.append(lines[i].strip())
            i += 1
        out.append(f'<p>{inline(" ".join(para))}</p>')
    return "\n".join(out)


def diagram(name: str, title: str, cards: list[tuple[str, str]]) -> None:
    """Original explanatory vector artwork; never modifies screenshot pixels."""
    e = html.escape
    svg = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1120 300" role="img" aria-labelledby="title desc"><title id="title">{e(title)}</title><desc id="desc">{e(". ".join(a+": "+b for a,b in cards))}</desc>',
           '<defs><marker id="arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8" fill="#A88A54"/></marker></defs>',
           '<rect width="1120" height="300" rx="8" fill="#121211"/>',
           f'<text x="32" y="38" fill="#C5BDAF" font-family="Georgia,serif" font-size="22">{e(title)}</text>']
    for i, (heading, body) in enumerate(cards):
        x = 28 + i * 278
        svg += [f'<rect x="{x}" y="70" width="230" height="194" rx="6" fill="#171717" stroke="#7E6A4D"/>',
                f'<text x="{x+18}" y="103" fill="#C5AC83" font-family="Georgia,serif" font-size="19">{e(heading)}</text>']
        for n, s in enumerate(textwrap.wrap(body, 24)):
            svg.append(f'<text x="{x+18}" y="{134+n*23}" fill="#C5BDAF" font-family="Arial,sans-serif" font-size="16">{e(s)}</text>')
        if i < len(cards)-1:
            svg.append(f'<path d="M{x+236},163 H{x+267}" fill="none" stroke="#A88A54" stroke-width="2" marker-end="url(#arrow)"/>')
    svg.append('</svg>')
    (HERE / "assets" / f"{name}.svg").write_text("\n".join(svg), encoding="utf-8")


def make_diagrams() -> None:
    definitions = {
        "action-flow": ("From words to a real result", [("Propose", "Name the person, action and exact terms."), ("Agree", "Both sides accept the same version."), ("Check", "Authority, resources and current safety must allow it."), ("Observe", "Read the result and check what the game actually did.")]),
        "knowledge": ("What happened is not what everyone knows", [("Event", "Something actually happens in the campaign."), ("Witness", "A participant learns what they experienced."), ("Report", "Other people hear a delivered account later."), ("Belief", "An account may be true, uncertain or false.")]),
        "family-flow": ("Player conception: a qualifying individual-chat route", [("Eligible adults", "The pairing and relationship must qualify."), ("Individual scene", "Qualifying intimate roleplay reaches its real choice."), ("Your choice", "Proceed allows checks. Pull Out stops this opportunity."), ("Possible result", "A valid roll may begin pregnancy. It is not guaranteed.")]),
        "government-flow": ("A proposal is not yet a law", [("Draft", "Choose real terms, target and effects."), ("Support", "Obtain the required sponsorship and adoption."), ("Present and vote", "The actual eligible voters decide under the constitution."), ("Verify", "Check enactment, resources and continuing obligations.")]),
        "guest-cycle": ("A visiting noble keeps a life outside your party", [("Original party", "Their own force becomes a protected waiting camp."), ("Visit", "The noble accompanies you under agreed terms."), ("Review", "Renew, discuss or begin a supported departure."), ("Return", "Travel back to the original force or safe alternative.")]),
        "arrest-flow": ("Accusation and custody are separate decisions", [("Prepare", "State the accusation within real jurisdiction."), ("Confirm", "Give the second explicit arrest decision."), ("Custody", "Surrender or a real duel/battle determines capture."), ("Later choice", "Release keeps the charge. Rescind clears that charge.")]),
        "economy-cycle": ("War puts pressure on a connected economy", [("Village households", "People support production and military enlistment."), ("Food and recruits", "Healthy villages supply more; damage limits output."), ("Military burden", "Recruitment, wages and supplies use real resources."), ("Recovery", "Peace and protection help; raids and losses leave damage.")]),
        "save-pair": ("Keep the matching save pair", [("Native save", "Bannerlord records its campaign state."), ("Reign snapshot", "Save Sync links matching memories and server state."), ("Protected point", "Both parts share the recorded save identity."), ("Restore", "Load the matched point, not unrelated later memory.")]),
    }
    for name, (title, cards) in definitions.items():
        diagram(name, title, cards)
    (HERE / "assets" / "requirements.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1120 400" role="img" aria-labelledby="title desc"><title id="title">Your PC and the cloud providers</title><desc id="desc">Bannerlord connects to the local Reign server and memory services. That server sends dialogue to a required cloud LLM provider. New optional images use a separately configured cloud image provider. Both cloud routes require internet and provider access.</desc><rect width="1120" height="400" rx="8" fill="#121211"/><g font-family="Georgia,serif" fill="#C5BDAF"><text x="32" y="40" font-size="22">Your PC runs Reign. Cloud providers generate new AI content.</text><g stroke="#7E6A4D" fill="#171717"><rect x="35" y="106" width="225" height="177" rx="6"/><rect x="310" y="106" width="300" height="177" rx="6"/><rect x="710" y="80" width="365" height="106" rx="6"/><rect x="710" y="223" width="365" height="106" rx="6"/></g><g font-size="22"><text x="60" y="149">Bannerlord + Reign</text><text x="335" y="149">Local Reign server</text><text x="735" y="118">Cloud LLM provider</text><text x="735" y="261">Cloud image provider</text></g><g font-family="Arial,sans-serif" font-size="18"><text x="60" y="186">Game, battles, UI</text><text x="60" y="217">and native actions</text><text x="335" y="186">Database, memory worker,</text><text x="335" y="217">campaign state and routing</text><text x="735" y="155">Required for AI dialogue</text><text x="735" y="298">Needed for optional new AI images</text><text x="35" y="367">Local hardware still needs room for the game AND Reign's services.</text></g></g><g fill="none" stroke="#A88A54" stroke-width="2"><path d="M260,195 H310"/><path d="M610,195 H660 V133 H710"/><path d="M660,195 V276 H710"/></g></svg>''', encoding="utf-8")


def main() -> None:
    monitor = runpy.run_path(str(HERE / "update-guide.py"))
    monitor["require_current"]()
    config = json.loads((HERE / "coverage-config.json").read_text(encoding="utf-8"))
    edition = date.fromisoformat(config["edition"])
    (HERE / "assets").mkdir(exist_ok=True)
    illustration_records = []
    for name in config["referenceImages"]:
        source = ROOT / "ReignBeta/artwork/ui-modern-style-kit/approved-references" / f"{name}.png"
        dest = HERE / "assets" / source.name
        shutil.copyfile(source, dest)
        illustration_records.append({"file": f"assets/{dest.name}", "type": "approved-layout-reference", "source": source.relative_to(ROOT).as_posix(), "sha256": hashlib.sha256(source.read_bytes()).hexdigest(), "liveGameplayEvidence": False})
    captures = json.loads((HERE / config["captureManifest"]).read_text(encoding="utf-8"))
    assert captures["schema"] == "reign-guide-native-captures-v1"
    for capture in captures["captures"]:
        assert capture["file"] not in {r["file"] for r in illustration_records}, "Capture still listed as a layout reference"
        source = Path(capture["originalPath"])
        assert hashlib.sha256(source.read_bytes()).hexdigest() == capture["sha256"], source
        assert image_dimensions(source) == (capture["width"], capture["height"])
        assert capture["transformations"] == "none"
        dest = (HERE / capture["file"]).resolve()
        assert dest.is_relative_to((HERE / "assets").resolve())
        shutil.copyfile(source, dest)
        illustration_records.append({**capture, "type": "native-game-screenshot", "source": capture["originalPath"], "provenance": "User-supplied in-game capture, byte-identical copy", "liveGameplayEvidence": True, "certifiesFeatureBehavior": False})
    make_diagrams()
    for path in sorted((HERE / "assets").glob("*.svg")):
        illustration_records.append({"file": "assets/"+path.name, "type": "original-explanatory-vector", "source": "docs/user-guide/build.py", "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "liveGameplayEvidence": False})

    chapters = []
    topics = []
    current = None
    for line in (HERE / "manual.md").read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            current = {"title": line[3:], "id": slug(line[3:]), "topics": []}
            chapters.append(current)
        elif line.startswith("### "):
            topic = {"title": line[4:], "id": slug(line[4:]), "chapter": current["title"], "chapterId": current["id"], "lines": []}
            current["topics"].append(topic)
            topics.append(topic)
        elif current and current["topics"]:
            current["topics"][-1]["lines"].append(line)
    ids = [x["id"] for x in chapters+topics]
    assert len(ids) == len(set(ids)), "Duplicate anchors"
    assert set(config["chapters"]) == {x["id"] for x in chapters}, "Chapter source mapping is incomplete"
    for chapter in chapters:
        chapter_config = config["chapters"][chapter["id"]]
        chapter["preview"] = bool(chapter_config.get("preview"))
        for topic in chapter["topics"]:
            raw = "\n".join(topic.pop("lines"))
            topic["html"] = render_markdown(raw.splitlines())
            topic["text"] = re.sub(r"<[^>]*>", " ", topic["html"])
            topic["text"] = re.sub(r"\s+", " ", html.unescape(topic["text"])).strip()
            topic["aliases"] = config["aliases"].get(topic["id"], [])
            topic["preview"] = chapter["preview"] or "> Preview:" in raw
    assert set(config["aliases"]).issubset({t["id"] for t in topics}), "Alias points to a nonexistent topic"
    source_paths = sorted(set(p for c in config["chapters"].values() for p in c["sources"]))
    source_records = []
    for path in source_paths:
        target = ROOT / path
        assert target.is_file(), f"Missing source: {path}"
        source_records.append({"path": path, "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
    coverage = {"schema": "reign-player-manual-coverage-v1", "edition": edition.isoformat(), "meaning": "Editorial coverage of reviewed behavior, not certification of every source file or native feature", "chapters": [{"id": c["id"], "title": c["title"], **config["chapters"][c["id"]], "topics": [t["id"] for t in c["topics"]]} for c in chapters], "sources": source_records, "illustrations": illustration_records, "explicitLimits": config["explicitLimits"]}
    (HERE / "coverage.json").write_text(json.dumps(coverage, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    search_data = [{k: t[k] for k in ("id", "title", "chapter", "chapterId", "text", "aliases", "preview")} for t in topics]
    (HERE / "search-data.json").write_text(json.dumps(search_data, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    nav = []
    body = []
    for n, c in enumerate(chapters, 1):
        badge = '<span class="badge">Preview included</span>' if c["preview"] else ''
        nav.append(f'<details><summary><a href="#{c["id"]}">{n:02} · {html.escape(c["title"])}</a>{badge}</summary><ul>' + "".join(f'<li><a href="#{t["id"]}">{html.escape(t["title"])}</a></li>' for t in c["topics"]) + '</ul></details>')
        topic_html = []
        for t in c["topics"]:
            topic_badge = '<span class="badge">Preview included</span>' if t["preview"] else ''
            topic_html.append(f'<article id="{t["id"]}" class="topic" tabindex="-1"><header><h3>{html.escape(t["title"])}</h3>{topic_badge}<a class="permalink" href="#{t["id"]}" aria-label="Link to {html.escape(t["title"])}">#</a></header>{t["html"]}</article>')
        body.append(f'<section id="{c["id"]}" class="chapter" tabindex="-1"><div class="chapter-heading"><p class="eyebrow">Chapter {n:02}</p><h2>{html.escape(c["title"])}</h2></div>' + "\n".join(topic_html) + '</section>')
    entries = {}
    for t in topics:
        for label in [t["title"], *t["aliases"]]:
            entries.setdefault(label.casefold(), (label, t))
    letters = sorted(set(label[0].upper() if label[0].isalpha() else '#' for label, _ in entries.values()))
    index_html = '<nav class="alphabet" aria-label="Index letters">' + "".join(f'<a href="#index-{letter}">{letter}</a>' for letter in letters) + '</nav>'
    for letter in letters:
        rows = sorted((v for v in entries.values() if (v[0][0].upper() if v[0][0].isalpha() else '#') == letter), key=lambda v:v[0].casefold())
        index_html += f'<section id="index-{letter}" class="index-letter"><h3>{letter}</h3><ul>' + "".join(f'<li><a href="#{t["id"]}">{html.escape(label)}</a>' + (' <span class="badge">Preview included</span>' if t['preview'] else '') + '</li>' for label,t in rows) + '</ul></section>'
    template = (HERE / "template.html").read_text(encoding="utf-8")
    values = {"CSS": (HERE/"guide.css").read_text(encoding="utf-8"), "JS": (HERE/"guide.js").read_text(encoding="utf-8"), "NAV": "\n".join(nav), "BODY": "\n".join(body), "INDEX": index_html, "SEARCH_DATA": json.dumps(search_data,ensure_ascii=False).replace("</", "<\\/"), "COUNTS": f'{len(chapters)} chapters · {len(topics)} topics · {len(illustration_records)} pictures'}
    values["EDITION_DATE"] = f"{edition:%B} {edition.day}, {edition.year}"
    for key,value in values.items():
        template = template.replace("{{"+key+"}}", value)
    assert not re.search(r"\{\{[A-Z_]+\}\}", template), "Unreplaced template value"
    (HERE/"index.html").write_text(template, encoding="utf-8")
    monitor["require_current"]()
    publication = runpy.run_path(str(HERE / "publish.py"))["publish_if_configured"]()
    if publication:
        print(json.dumps({"desktopPublication": publication}))
    print(json.dumps({"chapters":len(chapters), "topics":len(topics), "pictures":len(illustration_records), "sourceFiles":len(source_records), "words":sum(len(t['text'].split()) for t in topics), "htmlBytes":(HERE/'index.html').stat().st_size}))


if __name__ == "__main__":
    main()
