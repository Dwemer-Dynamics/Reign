"""Review-gated change detection for the Reign player guide; no runtime actions.

status is read-only unless --report is supplied. Both tracked and nonignored
untracked source are hashed in the client and paired server. accept advances
the baseline only from an exact, fully reviewed report, preserving its predecessor
on E:. This script never edits the manual or claims native acceptance.
"""
from __future__ import annotations

import argparse
import difflib
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import zipfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
BASELINE = HERE / "update-baseline.json"
ARCHIVE = Path("E:/CodexStorage/Projects/Reign/UserGuide/updates")
SCHEMA = "reign-guide-source-baseline-v1"
REPORT_SCHEMA = "reign-guide-change-review-v1"
TEXT = {".cs", ".csproj", ".props", ".targets", ".json", ".xml", ".md", ".txt", ".html", ".js", ".mjs", ".css", ".ps1", ".sh", ".cmd", ".py", ".toml", ".yaml", ".yml"}
CLIENT_PREFIXES = ("ReignBeta/src/", "ReignBeta/GUI/", "ReignBeta/ModuleData/", "ReignBeta/docs/", "ReignMcp/tests/", "docs/")
CLIENT_EXACT = {"AGENTS.md", "README.md", "REIGN_ROADMAP.md", "Directory.Build.props", "ReignBeta/SubModule.xml", "ReignBeta/ReignBeta.csproj", "ReignBeta/README.md", "ReignBeta/PortraitCache/shared-portrait-inventory.json"}
SERVER_PREFIXES = ("src/", "shared/", "ui/", "prompts/", "services/vector-worker/", "scripts/", "tests/", "docs/")
EXCLUDE = {"bin", "obj", "node_modules", "__pycache__", "generated-sources", "third_party", "third-party"}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def stable_id(inputs: dict) -> str:
    return digest(json.dumps(inputs, sort_keys=True, separators=(",", ":")).encode())


def git(repo: Path, *args: str) -> str:
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout.strip()


def watched(path: str, server: bool = False) -> bool:
    p = Path(path)
    if any(x.lower() in EXCLUDE for x in p.parts):
        return False
    if path.startswith("docs/user-guide/") or p.name.lower() in {".env", "settings.json", "credentials.json", "secrets.json"}:
        return False
    if server:
        return (path in {"AGENTS.md", "README.md", "ReignServer.csproj", "reign.repository.json"} or path.startswith(SERVER_PREFIXES)) and p.suffix.lower() in TEXT
    if path.startswith("ReignBeta/artwork/ui-modern-style-kit/approved-references/"):
        return p.suffix.lower() == ".png"
    if path in {"ReignBeta/artwork/ui-modern-style-kit/asset-manifest.json", "ReignBeta/artwork/ui-modern-style-kit/palette.json"}:
        return True
    return (path in CLIENT_EXACT or path.startswith(CLIENT_PREFIXES) or re.fullmatch(r"reign\.[^/]+\.json", path)) and p.suffix.lower() in TEXT


def config() -> dict:
    return json.loads((HERE / "coverage-config.json").read_text(encoding="utf-8"))


def snapshot() -> dict:
    inputs, revisions = {}, {}
    paired = ROOT / "ReignServer"
    if not paired.is_dir():
        raise RuntimeError("The paired ReignServer source is unavailable; monitoring is incomplete. Restore the source link before accepting a baseline.")
    for prefix, repo, server in [("", ROOT, False), ("ReignServer/", paired, True)]:
        revisions[prefix or "client"] = {"head":git(repo,"rev-parse","HEAD"), "branch":git(repo,"branch","--show-current")}
        names = git(repo,"ls-files","--cached","--others","--exclude-standard","-z").split("\0")
        for name in sorted(set(names)):
            if not name or not watched(name, server):
                continue
            p = repo / name
            if p.is_symlink():
                raise RuntimeError("Unreviewed source symlink in watched inputs: " + prefix + name)
            if not p.resolve().is_relative_to(repo.resolve()):
                raise RuntimeError("Watched source leaves its approved repository through a directory link: " + prefix + name)
            if not p.is_file():
                continue  # A missing previously baselined file becomes a removal.
            inputs[prefix+name] = digest(p.read_bytes())
    return {"inputs": inputs, "sourceSnapshotId":stable_id(inputs), "revisions":revisions}


def guide_digest() -> str:
    names = ["manual.md", "coverage-config.json", "template.html", "guide.css", "guide.js"]
    return stable_id({n:digest((HERE/n).read_bytes()) for n in names})


def preserve_source(now: dict) -> dict:
    if not Path("E:/CodexStorage").is_dir():
        raise RuntimeError("E: is unavailable; the exact source baseline cannot be preserved. Do not silently fall back to another drive.")
    folder=ARCHIVE/"source-snapshots"
    folder.mkdir(parents=True,exist_ok=True)
    target=folder/(now["sourceSnapshotId"]+".zip")
    if not target.exists():
        temporary=target.with_suffix(".partial")
        with zipfile.ZipFile(temporary,"w",compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
            for name,expected in now["inputs"].items():
                data=(ROOT/name).read_bytes()
                if digest(data)!=expected:
                    raise RuntimeError("Source changed while preserving baseline: " + name)
                z.writestr(name,data)
        with zipfile.ZipFile(temporary) as z:
            for name,expected in now["inputs"].items():
                if digest(z.read(name))!=expected:
                    raise RuntimeError("Source archive readback mismatch: " + name)
        temporary.replace(target)
    with zipfile.ZipFile(target) as z:
        if set(z.namelist())!=set(now["inputs"]):
            raise RuntimeError("Existing source archive has a different inventory.")
        for name,expected in now["inputs"].items():
            if digest(z.read(name))!=expected:
                raise RuntimeError("Existing source archive failed hash verification: " + name)
    return {"path":str(target),"sha256":digest(target.read_bytes()),"files":len(now["inputs"]),"meaning":"Exact source bytes, including uncommitted working contents; excludes runtime/private data and generated outputs."}


def chapters_for(path: str, cfg: dict) -> list[str]:
    overrides = sorted(cfg.get("monitorMappings", {}).items(), key=lambda p:-len(p[0]))
    for prefix, chapters in overrides:
        if path.startswith(prefix):
            if not set(chapters).issubset(cfg["chapters"]):
                raise RuntimeError("Invalid monitorMappings chapter for " + prefix)
            return chapters
    exact = [k for k,v in cfg["chapters"].items() if path in v["sources"]]
    if exact:
        return exact
    groups = {
        "arena":["arenas-tournaments-and-participation-xp"],
        "characters":["getting-to-know-people","town-life-residents-and-tavern-visits","relationships-and-starting-a-family","duels-arrests-rulers-and-civil-war"],
        "relationships":["relationships-and-starting-a-family","getting-to-know-people","events-and-npc-personal-lives"],
        "reputation":["gossip-reputation-and-public-standing"],
        "dialogue":["your-first-conversation","group-chat-letters-and-everyday-communication","events-and-npc-personal-lives","pictures-appearance-and-memories"],
        "diplomacy":["diplomacy-ambassadors-and-clan-accords"],
        "court":["ruling-a-kingdom","keeps-households-and-training","war-armies-and-temporary-guests","relationships-and-starting-a-family"],
        "government":["government-and-political-participation"],
        "spymaster":["spymaster-and-covert-work"],
        "kingdomevents":["time-news-and-world-events"],
        "rebellion":["duels-arrests-rulers-and-civil-war"],
        "worldsimulation":["economy-and-survival","war-armies-and-temporary-guests","duels-arrests-rulers-and-civil-war","time-news-and-world-events","your-first-conversation"],
        "portraits":["pictures-appearance-and-memories"],
        "persistence":["saving-loading-and-backups"],
        "ui":["pictures-appearance-and-memories","settings-and-the-control-center"],
        "platform":list(cfg["chapters"]), "core":list(cfg["chapters"])
    }
    match = re.search(r"/Modules/([^/]+)/", path)
    if match:
        return groups.get(match.group(1).lower(), [])  # New module needs explicit classification.
    lower = path.lower()
    if "/gui/" in lower or "/artwork/" in lower:
        return ["pictures-appearance-and-memories"]
    for key, chapters in groups.items():
        if key in lower:
            return chapters
    return list(cfg["chapters"])  # Shared contracts, setup and docs need broad review.


def changes_between(old: dict, new: dict, cfg: dict) -> list[dict]:
    rows = []
    for path in sorted(set(old)|set(new)):
        if old.get(path) == new.get(path):
            continue
        chapters = chapters_for(path,cfg)
        rows.append({"path":path, "change":"added" if path not in old else "removed" if path not in new else "modified", "before":old.get(path), "after":new.get(path), "candidateChapters":chapters, "needsClassification":not bool(chapters), "review":None})
    return rows


def load_baseline() -> dict:
    if not BASELINE.is_file():
        raise RuntimeError("No reviewed guide baseline. Run init --reviewed --note only after the initial editorial audit.")
    base=json.loads(BASELINE.read_text(encoding="utf-8"))
    if base.get("schema") != SCHEMA or base.get("sourceSnapshotId") != stable_id(base["inputs"]):
        raise RuntimeError("Baseline is invalid or was edited without an accepted review.")
    for name in base["inputs"]:
        if "\\" in name or Path(name).is_absolute() or ".." in Path(name).parts:
            raise RuntimeError("Invalid baseline source path.")
        allowed=watched(name[len("ReignServer/"):],True) if name.startswith("ReignServer/") else watched(name)
        if not allowed:
            raise RuntimeError("Baseline source is outside the declared watch scope: " + name)
    return base


def require_current() -> None:
    old, now = load_baseline(), snapshot()
    rows=changes_between(old["inputs"],now["inputs"],config())
    if rows:
        raise RuntimeError(f"Guide review required: {len(rows)} changed source inputs. Run update-guide.py status --report <review.json>, review each change and update the affected guide topics, then accept that exact report. Rebuilding cannot silently advance the source baseline.")


def validate_review(report: dict, base: dict, now: dict, cfg: dict) -> None:
    expected=changes_between(base["inputs"],now["inputs"],cfg)
    if report.get("schema") != REPORT_SCHEMA or report.get("baselineId") != base["sourceSnapshotId"] or report.get("sourceSnapshotId") != now["sourceSnapshotId"]:
        raise RuntimeError("Stale or unrelated review; regenerate status for the current source and baseline.")
    rows=report.get("changes",[])
    required=[(r["path"],r["change"],r["before"],r["after"]) for r in expected]
    actual=[(r.get("path"),r.get("change"),r.get("before"),r.get("after")) for r in rows]
    if required != actual:
        raise RuntimeError("Review omits, duplicates or alters current changes.")
    topics={re.sub(r"[^a-z0-9]+","-",line[4:].lower().replace("'", "").replace("’", "")).strip("-") for line in (HERE/"manual.md").read_text(encoding="utf-8").splitlines() if line.startswith("### ")}
    for expected_row,row in zip(expected,rows):
        if expected_row["needsClassification"]:
            raise RuntimeError("Classify the new module with monitorMappings before accepting: " + row["path"])
        review=row.get("review") or {}
        if review.get("outcome") not in {"updated","no-player-impact"} or len(review.get("reason","").strip()) < 12:
            raise RuntimeError("Every change needs an updated/no-player-impact decision and a meaningful reason: " + row["path"])
        if review["outcome"] == "updated" and (not review.get("topics") or not set(review["topics"]).issubset(topics)):
            raise RuntimeError("Updated changes must name actual revised guide topics: " + row["path"])
    if any((r.get("review") or {}).get("outcome")=="updated" for r in rows) and guide_digest()==base["guideDigest"]:
        raise RuntimeError("The review says updated, but guide content/configuration did not change.")


def write_baseline(now: dict, note: str, previous: dict | None = None, review: dict | None = None) -> None:
    if len(note.strip()) < 12:
        raise RuntimeError("Supply a meaningful editorial review note.")
    archive_receipt=None
    if previous:
        if not Path("E:/CodexStorage").is_dir():
            raise RuntimeError("E: is unavailable; preserve the existing baseline and defer acceptance.")
        ARCHIVE.mkdir(parents=True,exist_ok=True)
        original=BASELINE.read_bytes()
        saved=ARCHIVE/(datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")+"-"+previous["sourceSnapshotId"][:12]+".json")
        saved.write_bytes(original)
        if digest(saved.read_bytes()) != digest(original):
            raise RuntimeError("Prior baseline archive failed independent hash verification.")
        archive_receipt={"path":str(saved),"sha256":digest(original)}
        if review:
            saved.with_name(saved.stem+"-review.json").write_text(json.dumps(review,indent=2)+"\n",encoding="utf-8")
    source_archive=preserve_source(now)
    if snapshot()["sourceSnapshotId"]!=now["sourceSnapshotId"]:
        raise RuntimeError("Source changed before baseline publication; the existing baseline remains authoritative.")
    result={"schema":SCHEMA,"reviewedUtc":datetime.now(timezone.utc).isoformat(),"note":note,"scope":"Tracked plus nonignored untracked player-relevant client/server source, UI/configuration, authored data, focused docs/tests, setup and portrait/reference inventories. Runtime/private data and generated outputs are excluded.","guideDigest":guide_digest(),"sourceArchive":source_archive,"previous":archive_receipt,**now}
    temporary=BASELINE.with_suffix(".tmp")
    temporary.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    temporary.replace(BASELINE)


def main() -> int:
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("command",choices=["status","init","accept","diff"])
    p.add_argument("--report",type=Path)
    p.add_argument("--review",type=Path)
    p.add_argument("--note",default="")
    p.add_argument("--path")
    p.add_argument("--reviewed",action="store_true")
    args=p.parse_args()
    now=snapshot()
    if args.command=="init":
        if BASELINE.exists() or not args.reviewed:
            raise RuntimeError("init requires --reviewed and a missing baseline; existing baselines must use accept.")
        write_baseline(now,args.note)
        print(f"Initial reviewed baseline saved: {len(now['inputs'])} inputs; {now['sourceSnapshotId']}")
        return 0
    base=load_baseline()
    cfg=config()
    rows=changes_between(base["inputs"],now["inputs"],cfg)
    if args.command=="diff":
        name=(args.path or "").replace("\\","/")
        if name not in base["inputs"] and name not in now["inputs"]:
            raise RuntimeError("Choose an exact source path from the change report.")
        saved=base.get("sourceArchive")
        if not saved or not Path(saved["path"]).is_file():
            raise RuntimeError("Exact prior source archive is unavailable; do not invent a baseline from Git HEAD.")
        archive=Path(saved["path"])
        if digest(archive.read_bytes())!=saved["sha256"]:
            raise RuntimeError("Prior source archive failed SHA-256 verification.")
        with zipfile.ZipFile(archive) as z:
            before=z.read(name) if name in base["inputs"] else b""
        after=(ROOT/name).read_bytes() if name in now["inputs"] else b""
        if name in base["inputs"] and digest(before)!=base["inputs"][name]:
            raise RuntimeError("Prior source entry does not match baseline.")
        if name in now["inputs"] and digest(after)!=now["inputs"][name]:
            raise RuntimeError("Current source changed during diff; regenerate the status report.")
        try:
            print("".join(difflib.unified_diff(before.decode("utf-8-sig").splitlines(True),after.decode("utf-8-sig").splitlines(True),fromfile="reviewed/"+name,tofile="current/"+name)),end="")
        except UnicodeDecodeError:
            print(json.dumps({"path":name,"binary":True,"before":base["inputs"].get(name),"after":now["inputs"].get(name)}))
        return 0
    if args.command=="accept":
        if not args.review:
            raise RuntimeError("accept requires --review with every current change reviewed.")
        report=json.loads(args.review.read_text(encoding="utf-8"))
        validate_review(report,base,now,cfg)
        write_baseline(now,args.note,base,report)
        print(f"Accepted {len(rows)} reviewed source changes; previous baseline preserved on E:.")
        return 0
    report={"schema":REPORT_SCHEMA,"baselineId":base["sourceSnapshotId"],"sourceSnapshotId":now["sourceSnapshotId"],"observedUtc":datetime.now(timezone.utc).isoformat(),"revisions":now["revisions"],"changes":rows,"instructions":"For every change, read its actual diff/source. Update the guide or record why it has no player impact. Set review to {outcome: updated|no-player-impact, reason: a specific rationale, topics: actual changed topic IDs for updated}. Deferred or unclassified work blocks acceptance. Never accept solely to make a build pass."}
    if args.report:
        args.report.parent.mkdir(parents=True,exist_ok=True)
        args.report.write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"status":"review-required" if rows else "current","changes":len(rows),"unclassified":sum(r["needsClassification"] for r in rows),"watchedInputs":len(now["inputs"]),"baselineId":base["sourceSnapshotId"],"sourceSnapshotId":now["sourceSnapshotId"],"report":str(args.report) if args.report else None,"candidateChapters":sorted({c for r in rows for c in r["candidateChapters"]}),"paths":[r["path"] for r in rows][:25]},indent=2))
    return 2 if rows else 0


if __name__=="__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, OSError, ValueError) as e:
        raise SystemExit(str(e))
