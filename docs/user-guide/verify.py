"""Check manual content, local links, image provenance and source coverage."""
import hashlib
import json
import re
import runpy
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path

HERE = Path(__file__).resolve().parent


class Document(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = []
        self.links = []
        self.images = []
        self.network_resources = []
        self.headers = []
        self.scripts = []
        self.h1 = 0

    def handle_starttag(self, tag, values):
        a = dict(values)
        if "id" in a:
            self.ids.append(a["id"])
        if tag == "a" and "href" in a:
            self.links.append(a["href"])
        if tag == "img":
            self.images.append(a)
        if tag in {"img", "script", "link", "iframe", "video", "audio"}:
            source = a.get("src") or a.get("href", "")
            if re.match(r"(?:https?:)?//", source):
                self.network_resources.append(source)
        if tag == "script":
            self.scripts.append(a)
        if tag == "h1":
            self.h1 += 1


def main():
    content = (HERE / "index.html").read_text(encoding="utf-8")
    doc = Document()
    doc.feed(content)
    assert len(doc.ids) == len(set(doc.ids)), "Duplicate HTML IDs"
    assert not doc.network_resources, doc.network_resources
    missing = [h for h in doc.links if h.startswith("#") and h[1:] not in doc.ids]
    assert not missing, missing
    for h in doc.links:
        if not h.startswith(("#", "https://", "http://", "data:")):
            assert (HERE / h).is_file(), h
    assert "connect-src 'none'" in content
    assert all("src" not in s for s in doc.scripts), "External script dependency"
    assert "<form" not in content.lower(), "Unnecessary outbound form"
    assert all(i.get("alt") and i.get("src") for i in doc.images)
    for i in doc.images:
        assert (HERE / i["src"]).is_file(), i
        if i["src"].endswith(".svg"):
            ET.parse(HERE / i["src"])
    coverage = json.loads((HERE / "coverage.json").read_text(encoding="utf-8"))
    topics = json.loads((HERE / "search-data.json").read_text(encoding="utf-8"))
    config = json.loads((HERE / "coverage-config.json").read_text(encoding="utf-8"))
    captures = json.loads((HERE / config["captureManifest"]).read_text(encoding="utf-8"))["captures"]
    capture_files = {r["file"] for r in captures}
    dimensions = runpy.run_path(str(HERE / "build.py"))["image_dimensions"]
    root = HERE.parents[1]
    source_paths = {r["path"] for r in coverage["sources"]}
    for record in coverage["sources"]:
        assert hashlib.sha256((root / record["path"]).read_bytes()).hexdigest() == record["sha256"], "Source changed after review: " + record["path"]
    for chapter in coverage["chapters"]:
        assert chapter["sources"] and set(chapter["sources"]).issubset(source_paths)
    assert {t["id"] for t in topics} == {t for c in coverage["chapters"] for t in c["topics"]}
    used_images = {i["src"] for i in doc.images}
    assert used_images == {r["file"] for r in coverage["illustrations"]}, "Orphan/missing illustration"
    for r in coverage["illustrations"]:
        b = (HERE / r["file"]).read_bytes()
        assert hashlib.sha256(b).hexdigest() == r["sha256"], r["file"]
        if r["type"] == "approved-layout-reference":
            assert b == (root / r["source"]).read_bytes(), r["source"]
        if r["type"] == "native-game-screenshot":
            assert r["file"] in capture_files and r["transformations"] == "none"
            assert b == Path(r["originalPath"]).read_bytes()
            assert dimensions(HERE / r["file"]) == (r["width"], r["height"])
            assert r["liveGameplayEvidence"] is True and r["certifiesFeatureBehavior"] is False
            img = next(i for i in doc.images if i["src"] == r["file"])
            assert img["width"] == str(r["width"]) and img["height"] == str(r["height"])
            assert img["alt"].startswith("In-game screenshot")
        else:
            assert r["liveGameplayEvidence"] is False
    assert capture_files == {r["file"] for r in coverage["illustrations"] if r["type"] == "native-game-screenshot"}
    for t in topics:
        assert t["id"] in doc.ids
        assert t["text"] and t["title"]
    manual = (HERE / "manual.md").read_text(encoding="utf-8")
    reference = json.loads((HERE / "action-reference.json").read_text(encoding="utf-8"))
    assert reference["edition"] == coverage["edition"]
    expected_ids = [entry for group in reference["definitionGroups"].values() for entry in group]
    listed_ids = [entry for row in reference["entries"] for entry in row["ids"]]
    assert expected_ids and sorted(expected_ids) == sorted(listed_ids), "Incomplete action reference"
    assert len(listed_ids) == len(set(listed_ids)), "Duplicate action ID"
    action_chapter = manual.split("\n## Action sentence system\n", 1)[1].split("\n## ", 1)[0]
    action_labels = re.findall(r"^\| \*\*(.*?)\*\* \|", action_chapter, re.M)
    assert action_labels == [row["label"] for row in reference["entries"]], "Action table drift"
    action_topics = {t["id"]: t["text"] for t in topics if t["id"] in next(c["topics"] for c in coverage["chapters"] if c["id"] == "action-sentence-system")}
    assert all(row["label"] in action_topics[row["topicId"]] for row in reference["entries"])
    assert all(label in action_chapter for label in reference["specialMarriageControls"])
    archived = reference["catalogArchive"]
    assert hashlib.sha256(Path(archived["path"]).read_bytes()).hexdigest() == archived["sha256"], "Action source archive drift"
    # Hardware planning has requested numeric specifications. Gameplay omits tuning formulas.
    gameplay = manual.split("## Start here", 1)[1]
    assert not re.search(r"\d\s*%|\d\s*×|\b(?:chance|probability)\s*[:=]\s*\d", gameplay, re.I)
    assert "ButterLib" in manual
    assert "cloud LLM provider is required" in manual
    assert "cloud image provider is also required" in manual
    assert "does not have a verified RAM minimum or typical-use range" in manual
    assert "Plan around 32 GB" not in manual
    assert "systemRamGB\": null" in (HERE / "requirements-evidence.json").read_text(encoding="utf-8")
    for topic_id, keys in {
        "asking-for-a-real-action":["Alt+A", "Enter", "Escape"],
        "approach-speak-and-listen":["Alt+A", "Enter"],
        "party-chat-and-private-audiences":["backslash", "Alt+A", "Enter", "Escape"],
        "correspondence-sending-and-receiving-letters":["Ctrl+M", "Alt+A", "Enter", "Escape"],
        "giving-a-clear-campaign-order":["Alt+A", "Ctrl+M"],
        "inviting-a-noble-to-travel-with-you":["Alt+A", "backslash"],
        "proposals-sponsors-parties-and-sessions":["Alt+A", "Escape"]
    }.items():
        text = next(t["text"] for t in topics if t["id"]==topic_id)
        assert all(key in text for key in keys), "Missing in-context shortcut: " + topic_id
    action_source=(root/"ReignBeta/src/Modules/Dialogue/UI/ReignActionBuilder.cs").read_text(encoding="utf-8")
    assert "InputKey.LeftAlt" in action_source and "InputKey.RightAlt" in action_source and "IsKeyReleased(InputKey.A)" in action_source
    correspondence_source=(root/"ReignBeta/src/Modules/Dialogue/UI/ReignCorrespondenceScreenManager.cs").read_text(encoding="utf-8")
    assert "InputKey.LeftControl" in correspondence_source and "IsKeyPressed(InputKey.M)" in correspondence_source
    party_source=(root/"ReignBeta/src/Modules/Dialogue/UI/ReignPartyChatScreenManager.cs").read_text(encoding="utf-8")
    assert "InputKey.BackSlash" in party_source
    assert "only through a qualifying intimate event" in manual
    assert len(config["aliases"]) >= 90
    print(json.dumps({"ok": True, "chapters":len(coverage["chapters"]), "topics":len(topics), "pictures":len(doc.images), "anchors":len(doc.ids), "sources":len(source_paths), "indexEntries":sum(1+len(t["aliases"]) for t in topics), "automaticNetworkDependencies":len(doc.network_resources), "checked":"Links, aliases, current source hashes, exact picture bytes, SVG XML, offline resources, alt text and no gameplay-tuning formulas"}))


if __name__ == "__main__":
    main()
