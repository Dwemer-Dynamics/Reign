#!/usr/bin/env python3
"""Evidence-first inventory, visual comparison, and acceptance dashboard."""
import argparse, hashlib, json, math, xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
CATALOG = Path(__file__).parent / "calibration" / "ui-catalog.json"
NATIVE_REVIEW_ATTESTATION_SCHEMA = "reign-ui-native-visual-review-attestation-v1"
NATIVE_REVIEW_CONFIRMATION = "I confirm this native capture matches the approved Reign reference for all fixed visuals and typography"
NATIVE_RUNTIME_STATE_ACCEPTANCE_SCHEMA = "reign-ui-native-runtime-state-acceptance-v1"
NATIVE_RUNTIME_STATE_RECEIPT_SCHEMA = "reign-ui-native-runtime-state-receipt-v1"
NATIVE_REVIEW_CRITERIA = (
    "approvedFixedVisualsExact",
    "differencesConfinedToDeclaredDynamicRegions",
    "serifFaceAndWeightMatch",
    "typographicHierarchyMatches",
    "capitalizationAndTrackingMatch",
    "semanticTextColorsMatch",
    "alignmentBaselineAndWrappingMatch",
    "nativeGlyphRenderingAccepted",
    "noUnexpectedColorsOrShapes",
)

def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def combined_digest(paths):
    value=hashlib.sha256()
    for item in sorted((Path(path).resolve() for path in paths),key=lambda path:path.as_posix()):
        value.update(item.relative_to(ROOT).as_posix().encode("utf-8")); value.update(b"\0")
        value.update(item.read_bytes()); value.update(b"\0")
    return value.hexdigest()

def read_json(path): return json.loads(Path(path).read_text(encoding="utf-8-sig"))

def find_surface(catalog,target_id):
    for item in catalog["interfaces"]:
        if item["id"]==target_id:
            if item.get("supportUi"):
                raise ValueError(f"Catalog target '{target_id}' is previewer-only and has no native acceptance surface.")
            return dict(item), "runtime"
    for item in catalog["nativeAugmentations"]["targets"]:
        if item["id"]==target_id: return dict(item), "native-augmentation"
    raise ValueError(f"Unknown catalog target '{target_id}'.")

def find_runtime_state(catalog,target_id,state_id):
    surface,surface_type=find_surface(catalog,target_id)
    if surface_type!="runtime": raise ValueError("Nested runtime-state acceptance requires a standalone runtime parent.")
    matches=[dict(item) for item in surface.get("previewStates",[]) if item.get("id")==state_id]
    if len(matches)!=1: raise ValueError(f"Catalog runtime state '{state_id}' is not declared exactly once beneath '{target_id}'.")
    state=matches[0]
    if state.get("previewOnly") is not True or state.get("nativeInjection") is not False:
        raise ValueError("Nested runtime state must remain preview-only catalog data with nativeInjection=false.")
    native_evidence=state.get("nativeEvidence")
    if not isinstance(native_evidence,dict) or native_evidence.get("required") is not True:
        raise ValueError("Nested runtime state does not declare required native evidence.")
    if native_evidence.get("matrix")!="inherit-parent": raise ValueError("Nested runtime-state matrix must be inherit-parent.")
    expected_relative=f"states/{state_id}/acceptance.json"
    if native_evidence.get("acceptanceRelativePath")!=expected_relative:
        raise ValueError(f"Nested runtime-state acceptance path must be {expected_relative}.")
    if native_evidence.get("statusReceiptCommand")!="ui_status": raise ValueError("Nested runtime-state status receipt command must be ui_status.")
    if not isinstance(native_evidence.get("statusAssertions"),dict) or not native_evidence["statusAssertions"]:
        raise ValueError("Nested runtime state declares no native status assertions.")
    if not isinstance(native_evidence.get("requiredVisibleWidgetIds"),list) or not native_evidence["requiredVisibleWidgetIds"]:
        raise ValueError("Nested runtime state declares no required visible widgets.")
    return surface,state

def runtime_state_setup_action(state):
    declared=str(state.get("nativeEvidence",{}).get("setupAction","")).strip()
    if not declared: raise ValueError(f"Catalog runtime state '{state.get('id','')}' has no native-evidence setup action.")
    return declared

def require_matrix_case(catalog,resolution,ui_scale):
    for item in catalog["targetMatrix"]:
        if item["resolution"]==resolution and any(abs(float(scale)-ui_scale)<0.0001 for scale in item["uiScales"]): return
    raise ValueError(f"{resolution}@{ui_scale:.2f} is not a required catalog matrix case.")

def render_base_hash(report,target):
    hashes={
        case.get("nativeAugmentation",{}).get("baseSha256","")
        for case in report.get("cases",[])
        if case.get("caseId","").startswith(f"NativeAugmentation-{target}-")
           and case.get("nativeAugmentation",{}).get("baseSha256")
    }
    if len(hashes)!=1: raise ValueError(f"Rendered audit does not prove one exact installed base-prefab hash for {target}.")
    return next(iter(hashes))

def validate_snapshot(snapshot_path,surface,surface_type,source_sha):
    if snapshot_path is None:
        if surface_type!="native-augmentation": raise ValueError("A runtime snapshot is required for standalone runtime acceptance.")
        return None
    snapshot=read_json(snapshot_path)
    if snapshot.get("schema")!="reign-ui-runtime-snapshot-v1": raise ValueError("Runtime snapshot schema is not reign-ui-runtime-snapshot-v1.")
    if "supportUi" in snapshot:
        raise ValueError("Runtime snapshot contains previewer-only support UI evidence instead of target-only native evidence.")
    if "supportUiInjected" not in snapshot or snapshot.get("supportUiInjected") is not False:
        raise ValueError("Runtime snapshot does not explicitly prove supportUiInjected=false.")
    if snapshot.get("visualSupportUiInjected") is True:
        raise ValueError("Runtime snapshot reports visual support UI injection.")
    if surface_type=="runtime":
        if snapshot.get("movieName")!=surface["movie"]: raise ValueError("Runtime snapshot movie does not match the catalog target.")
        if snapshot.get("prefabSha256")!=source_sha: raise ValueError("Runtime snapshot prefab hash does not match current source.")
        if not snapshot.get("widgets"): raise ValueError("Runtime snapshot contains no widgets.")
    elif not snapshot.get("widgets"):
        raise ValueError("Optional native-augmentation snapshot contains no widgets.")
    return snapshot

def native_review_attestation(reviewed,review_confirmation):
    confirmation=(review_confirmation or "").strip()
    if reviewed and confirmation!=NATIVE_REVIEW_CONFIRMATION:
        raise ValueError(f"Reviewed native acceptance requires --review-confirmation '{NATIVE_REVIEW_CONFIRMATION}'.")
    if not reviewed and confirmation:
        raise ValueError("--review-confirmation is valid only together with --reviewed.")
    return {
        "schema":NATIVE_REVIEW_ATTESTATION_SCHEMA,
        "version":1,
        "confirmation":confirmation,
        "reviewedUtc":datetime.now(timezone.utc).isoformat() if reviewed else None,
        "criteria":{key:bool(reviewed) for key in NATIVE_REVIEW_CRITERIA}
    }

def validate_native_evidence(resolution,screenshot,capture_metadata,snapshot,diagnostic_count):
    screenshot=screenshot.resolve(); snapshot=snapshot.resolve() if snapshot else None
    if not screenshot.is_file(): raise ValueError(f"Native screenshot is missing: {screenshot}")
    capture_metadata=(capture_metadata or Path(str(screenshot)+".json")).resolve()
    if not capture_metadata.is_file(): raise ValueError(f"Verified native capture metadata is missing: {capture_metadata}")
    expected_size=tuple(int(value) for value in resolution.lower().split("x",1))
    with Image.open(screenshot) as image:
        if image.size!=expected_size: raise ValueError(f"Native screenshot is {image.size[0]}x{image.size[1]}, expected {resolution}.")
    capture=read_json(capture_metadata)
    if capture.get("schema")!="reign-ui-window-capture-v1": raise ValueError("Native capture metadata schema is not reign-ui-window-capture-v1.")
    if "bannerlord" not in str(capture.get("processName","")).lower(): raise ValueError("Native capture metadata is not from a Bannerlord process.")
    allowed_capture_modes={"verified-foreground-client-area","verified-foreground-print-window-client-area","verified-foreground-steam-backbuffer"}
    if capture.get("captureMode") not in allowed_capture_modes: raise ValueError("Native capture metadata is not foreground-client verified.")
    if (int(capture.get("width",0)),int(capture.get("height",0)))!=expected_size: raise ValueError("Native capture metadata dimensions do not match the catalog case.")
    if Path(capture.get("path","")).resolve()!=screenshot: raise ValueError("Native capture metadata points to another screenshot.")
    if capture.get("sha256")!=digest(screenshot): raise ValueError("Native capture metadata does not bind the screenshot hash.")
    if snapshot and not snapshot.is_file(): raise ValueError(f"Runtime snapshot is missing: {snapshot}")
    if diagnostic_count<0: raise ValueError("Diagnostic count cannot be negative.")
    return screenshot,capture_metadata,snapshot,expected_size

def runtime_source_hashes(surface,installed_root):
    source=ROOT/surface["prefab"]
    if not source.is_file(): raise ValueError(f"Workspace prefab is missing: {source}")
    if installed_root is None: raise ValueError("--installed-root is required for standalone runtime acceptance.")
    installed=installed_root.resolve()/surface["prefab"]
    if not installed.is_file(): raise ValueError(f"Installed prefab is missing: {installed}")
    source_sha=digest(source); installed_sha=digest(installed)
    if source_sha!=installed_sha: raise ValueError("Installed prefab hash differs from workspace source; deploy the validated source before recording acceptance.")
    return source_sha,installed_sha

def validate_runtime_snapshot_case(snapshot,surface,source_sha,expected_size):
    native_snapshot=validate_snapshot(snapshot,surface,"runtime",source_sha)
    physical=(int(round(float(native_snapshot.get("physicalWidth",0)))),int(round(float(native_snapshot.get("physicalHeight",0)))))
    if physical!=expected_size: raise ValueError(f"Runtime snapshot is {physical[0]}x{physical[1]}, expected {expected_size[0]}x{expected_size[1]}.")
    runtime_ui_scale=float(native_snapshot.get("uiScale",0))
    if not math.isfinite(runtime_ui_scale) or runtime_ui_scale<=0: raise ValueError("Runtime snapshot UIContext.CustomScale must be a positive finite number.")
    return native_snapshot,runtime_ui_scale

def validate_runtime_state_receipt(receipt_path,target_id,state,setup_action):
    state_id=state["id"]
    native_evidence=state["nativeEvidence"]
    receipt_path=receipt_path.resolve()
    if not receipt_path.is_file(): raise ValueError(f"Runtime-state receipt is missing: {receipt_path}")
    receipt=read_json(receipt_path)
    if receipt.get("schema")!=NATIVE_RUNTIME_STATE_RECEIPT_SCHEMA: raise ValueError("Runtime-state receipt schema mismatch.")
    if receipt.get("parentTargetId")!=target_id: raise ValueError("Runtime-state receipt parent target mismatch.")
    if receipt.get("stateId")!=state_id: raise ValueError("Runtime-state receipt state id mismatch.")
    if receipt.get("setupAction")!=setup_action: raise ValueError("Runtime-state receipt setup action mismatch.")
    if receipt.get("statusReceiptCommand")!=native_evidence.get("statusReceiptCommand"):
        raise ValueError("Runtime-state receipt command does not match the catalog contract.")
    if receipt.get("actionCompleted") is not True: raise ValueError("Runtime-state setup action did not complete.")
    assertions=receipt.get("statusAssertions")
    expected=native_evidence.get("statusAssertions")
    if not isinstance(assertions,dict) or not isinstance(expected,dict): raise ValueError("Runtime-state status assertions are missing.")
    if set(assertions)!=set(expected): raise ValueError("Runtime-state receipt assertion keys do not exactly match the catalog contract.")
    failed=[key for key in expected if assertions.get(key) is not True]
    if failed: raise ValueError("Runtime-state receipt failed required assertions: "+", ".join(failed))
    if receipt.get("allPassed") is not True: raise ValueError("Runtime-state receipt does not report allPassed=true.")
    return receipt_path,receipt

def record_acceptance(target_id,resolution,ui_scale,screenshot,capture_metadata,snapshot,evidence_root,installed_root,render_report,diagnostic_count,reviewed,review_confirmation):
    catalog=read_json(CATALOG); surface,surface_type=find_surface(catalog,target_id); require_matrix_case(catalog,resolution,ui_scale)
    screenshot,capture_metadata,snapshot,expected_size=validate_native_evidence(
        resolution,screenshot,capture_metadata,snapshot,diagnostic_count)

    source_sha=installed_sha=base_sha=patch_sha=""
    if surface_type=="runtime":
        source_sha,installed_sha=runtime_source_hashes(surface,installed_root)
    else:
        sources=[ROOT/path for path in surface["sources"]]
        missing=[str(path) for path in sources if not path.is_file()]
        if missing: raise ValueError("Native patch source is missing: "+", ".join(missing))
        patch_sha=combined_digest(sources)
        base_sha=render_base_hash(read_json(render_report),surface["target"])

    native_snapshot=validate_snapshot(snapshot,surface,surface_type,source_sha)
    runtime_ui_scale=None
    if native_snapshot:
        physical=(int(round(float(native_snapshot.get("physicalWidth",0)))),int(round(float(native_snapshot.get("physicalHeight",0)))))
        if physical!=expected_size: raise ValueError(f"Runtime snapshot is {physical[0]}x{physical[1]}, expected {resolution}.")
        runtime_ui_scale=float(native_snapshot.get("uiScale",0))
        if not math.isfinite(runtime_ui_scale) or runtime_ui_scale<=0: raise ValueError("Runtime snapshot UIContext.CustomScale must be a positive finite number.")

    target_root=evidence_root.resolve()/target_id; manifest_path=target_root/"acceptance.json"
    existing=read_json(manifest_path) if manifest_path.is_file() else {}
    if existing and (existing.get("schema")!="reign-ui-native-parity-acceptance-v1" or existing.get("targetId")!=target_id):
        raise ValueError(f"Existing acceptance manifest has the wrong schema or target: {manifest_path}")
    review_attestation=native_review_attestation(reviewed,review_confirmation)
    case={
        "resolution":resolution,"uiScale":ui_scale,"runtimeUiScale":runtime_ui_scale,"reviewed":bool(reviewed),"diagnosticCount":diagnostic_count,
        "reviewAttestation":review_attestation,
        "screenshotPath":str(screenshot),"screenshotSha256":digest(screenshot),
        "captureMetadataPath":str(capture_metadata),"captureMetadataSha256":digest(capture_metadata),
        "snapshotPath":str(snapshot) if snapshot else "","snapshotSha256":digest(snapshot) if snapshot else ""
    }
    cases={f"{item.get('resolution')}@{float(item.get('uiScale',0)):.2f}":item for item in existing.get("cases",[])}
    cases[f"{resolution}@{ui_scale:.2f}"]=case
    order=[f"{item['resolution']}@{float(scale):.2f}" for item in catalog["targetMatrix"] for scale in item["uiScales"]]
    manifest={
        "schema":"reign-ui-native-parity-acceptance-v1","generatedUtc":datetime.now(timezone.utc).isoformat(),
        "targetId":target_id,"surfaceType":surface_type,"sourceSha256":source_sha,"installedSha256":installed_sha,
        "basePrefabSha256":base_sha,"patchSourceSha256":patch_sha,
        "cases":[cases[key] for key in order if key in cases]
    }
    target_root.mkdir(parents=True,exist_ok=True); manifest_path.write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    return {"ok":True,"manifestPath":str(manifest_path),"targetId":target_id,"surfaceType":surface_type,"case":case,"caseCount":len(manifest["cases"])}

def record_runtime_state_acceptance(target_id,state_id,resolution,ui_scale,screenshot,capture_metadata,snapshot,state_receipt,evidence_root,installed_root,render_report,diagnostic_count,reviewed,review_confirmation):
    catalog=read_json(CATALOG); surface,state=find_runtime_state(catalog,target_id,state_id); require_matrix_case(catalog,resolution,ui_scale)
    setup_action=runtime_state_setup_action(state)
    screenshot,capture_metadata,snapshot,expected_size=validate_native_evidence(
        resolution,screenshot,capture_metadata,snapshot,diagnostic_count)
    if snapshot is None: raise ValueError("A runtime snapshot is required for nested runtime-state acceptance.")
    source_sha,installed_sha=runtime_source_hashes(surface,installed_root)
    native_snapshot,runtime_ui_scale=validate_runtime_snapshot_case(snapshot,surface,source_sha,expected_size)
    widgets=native_snapshot.get("widgets",[])
    for widget_id in state["nativeEvidence"].get("requiredVisibleWidgetIds",[]):
        matches=[item for item in widgets if item.get("id")==widget_id]
        if len(matches)!=1: raise ValueError(f"Runtime-state snapshot does not contain exactly one required widget '{widget_id}'.")
        if matches[0].get("isVisible") is not True: raise ValueError(f"Runtime-state snapshot reports required widget '{widget_id}' hidden.")
    for widget_id in state["nativeEvidence"].get("requiredEnabledWidgetIds",[]):
        matches=[item for item in widgets if item.get("id")==widget_id]
        if len(matches)!=1: raise ValueError(f"Runtime-state snapshot does not contain exactly one required enabled widget '{widget_id}'.")
        if matches[0].get("isEnabled") is not True:
            raise ValueError(f"Runtime-state snapshot reports required widget '{widget_id}' disabled.")
    receipt_path,_=validate_runtime_state_receipt(state_receipt,target_id,state,setup_action)

    manifest_path=evidence_root.resolve()/target_id/"states"/state_id/"acceptance.json"
    existing=read_json(manifest_path) if manifest_path.is_file() else {}
    if existing and (existing.get("schema")!=NATIVE_RUNTIME_STATE_ACCEPTANCE_SCHEMA
                     or existing.get("parentTargetId")!=target_id
                     or existing.get("stateId")!=state_id):
        raise ValueError(f"Existing runtime-state acceptance manifest has the wrong schema or identity: {manifest_path}")
    review_attestation=native_review_attestation(reviewed,review_confirmation)
    case={
        "resolution":resolution,"uiScale":ui_scale,"runtimeUiScale":runtime_ui_scale,"reviewed":bool(reviewed),"diagnosticCount":diagnostic_count,
        "reviewAttestation":review_attestation,
        "screenshotPath":str(screenshot),"screenshotSha256":digest(screenshot),
        "captureMetadataPath":str(capture_metadata),"captureMetadataSha256":digest(capture_metadata),
        "snapshotPath":str(snapshot),"snapshotSha256":digest(snapshot),
        "stateReceiptPath":str(receipt_path),"stateReceiptSha256":digest(receipt_path)
    }
    cases={f"{item.get('resolution')}@{float(item.get('uiScale',0)):.2f}":item for item in existing.get("cases",[])}
    cases[f"{resolution}@{ui_scale:.2f}"]=case
    order=[f"{item['resolution']}@{float(scale):.2f}" for item in catalog["targetMatrix"] for scale in item["uiScales"]]
    manifest={
        "schema":NATIVE_RUNTIME_STATE_ACCEPTANCE_SCHEMA,"generatedUtc":datetime.now(timezone.utc).isoformat(),
        "parentTargetId":target_id,"stateId":state_id,"surfaceType":"runtime-state","matrix":state["nativeEvidence"]["matrix"],
        "movie":surface["movie"],"variant":state.get("variant","").strip(),"setupAction":setup_action,
        "sourceSha256":source_sha,"installedSha256":installed_sha,
        "cases":[cases[key] for key in order if key in cases]
    }
    manifest_path.parent.mkdir(parents=True,exist_ok=True); manifest_path.write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    return {"ok":True,"manifestPath":str(manifest_path),"parentTargetId":target_id,"stateId":state_id,
            "surfaceType":"runtime-state","case":case,"caseCount":len(manifest["cases"])}

def inventory(output, installed_root=None):
    catalog = json.loads(CATALOG.read_text(encoding="utf-8")); rows = []
    for declared in catalog["interfaces"]:
        row = dict(declared); path = ROOT / row["prefab"]
        previewer_only=bool(row.get("supportUi"))
        row.update(exists=path.is_file(), sha256="", bindings=[], sprites=[], ids=[], duplicateIds=[], stableIdentityCoverage=0,
                   nativeAcceptanceRequired=not previewer_only, state="missing-prefab")
        if installed_root:
            installed_path=installed_root / row["prefab"]
            row.update(installedPath=str(installed_path.resolve()), installedExists=installed_path.is_file(), installedSha256=digest(installed_path) if installed_path.is_file() else "")
        if path.is_file():
            root = ET.fromstring(path.read_text(encoding="utf-8-sig")); nodes=list(root.iter()); ids=[]; bindings=set(); sprites=set()
            for node in nodes:
                if node.attrib.get("Id"): ids.append(node.attrib["Id"])
                for key,value in node.attrib.items():
                    if value.startswith("@"): bindings.add(value[1:].split(".",1)[0])
                    if key in ("Sprite","Brush") and value: sprites.add(value)
            row.update(sha256=digest(path), bindings=sorted(bindings), sprites=sorted(sprites), ids=ids,
                       duplicateIds=sorted({x for x in ids if ids.count(x)>1}),
                       stableIdentityCoverage=round(len(ids)/max(1,len(nodes)),4),
                       state="previewer-only" if previewer_only else "needs-live-evidence")
            if installed_root:
                row["installedMatchesSource"]=row["installedExists"] and row["installedSha256"]==row["sha256"]
        rows.append(row)
    result={"schema":"reign-ui-inventory-v1","generatedUtc":datetime.now(timezone.utc).isoformat(),"targetMatrix":catalog["targetMatrix"],"interfaces":rows}
    output.parent.mkdir(parents=True,exist_ok=True); output.write_text(json.dumps(result,indent=2),encoding="utf-8"); return result

def compare(reference_path, live_path, output_dir, snapshot_path=None):
    output_dir.mkdir(parents=True,exist_ok=True); ref=Image.open(reference_path).convert("RGBA"); live=Image.open(live_path).convert("RGBA")
    alignment={"method":"exact","offsetX":0,"offsetY":0}
    content_box=(0,0,live.width,live.height)
    if ref.size != live.size:
        source=ref.size; ref.thumbnail(live.size,Image.Resampling.LANCZOS); fitted=ref.size; canvas=Image.new("RGBA",live.size,(0,0,0,0)); offset=((live.width-ref.width)//2,(live.height-ref.height)//2); canvas.alpha_composite(ref,offset); ref=canvas
        content_box=(offset[0],offset[1],offset[0]+fitted[0],offset[1]+fitted[1])
        alignment={"method":"fit-center","offsetX":offset[0],"offsetY":offset[1],"sourceSize":source,"contentBounds":content_box}
    delta=ImageChops.difference(ref,live); gray=delta.convert("L"); histogram=gray.histogram(); pixels=live.width*live.height
    content_gray=gray.crop(content_box); content_histogram=content_gray.histogram(); content_pixels=content_gray.width*content_gray.height
    overlay=Image.blend(ref,live,.5); heat=Image.new("RGBA",live.size,(255,32,32,0)); heat.putalpha(gray.point(lambda x:min(255,x*4)))
    overlay_path=output_dir/"aligned-overlay.png"; diff_path=output_dir/"difference.png"; overlay.save(overlay_path); heat.save(diff_path)
    elements=[]
    if snapshot_path:
        snapshot=json.loads(snapshot_path.read_text(encoding="utf-8-sig"))
        for w in snapshot.get("widgets",[]):
            x,y,width,height=[float(w.get(k,0)) for k in ("logicalX","logicalY","logicalWidth","logicalHeight")]
            elements.append({"id":w.get("id",""),"path":w.get("path",""),"bounds":[x,y,width,height],"clippedToViewport":x<0 or y<0 or x+width>live.width or y+height>live.height,"changes":w.get("changes",{})})
    report={"schema":"reign-ui-visual-comparison-v1","createdUtc":datetime.now(timezone.utc).isoformat(),
      "reference":{"path":str(reference_path.resolve()),"sha256":digest(reference_path)},"live":{"path":str(live_path.resolve()),"sha256":digest(live_path),"size":live.size},"alignment":alignment,
      "metrics":{"meanAbsoluteDifference":round(sum(i*c for i,c in enumerate(histogram))/max(1,pixels),4),"changedPixelRatioAt16":round(sum(histogram[16:])/max(1,pixels),6),"differenceBounds":gray.getbbox(),
                 "contentMeanAbsoluteDifference":round(sum(i*c for i,c in enumerate(content_histogram))/max(1,content_pixels),4),"contentChangedPixelRatioAt16":round(sum(content_histogram[16:])/max(1,content_pixels),6),"contentDifferenceBounds":content_gray.getbbox()},
      "artifacts":{"overlay":str(overlay_path.resolve()),"difference":str(diff_path.resolve())},"elements":elements}
    (output_dir/"fit-report.json").write_text(json.dumps(report,indent=2),encoding="utf-8"); return report

def contact_sheet(batch_report_path, output_path):
    batch=read_json(batch_report_path)
    if batch.get("schema")!="reign-ui-native-capture-batch-v1": raise ValueError("Batch report schema is not reign-ui-native-capture-batch-v1.")
    surface_rows=[dict(item,contactSheetKind="surface") for item in batch.get("cases",[])
                  if item.get("status")=="staged-for-review" and item.get("screenshotPath")]
    state_rows=[dict(item,contactSheetKind="runtime-state") for item in batch.get("stateCases",[])
                if item.get("status")=="staged-for-review" and item.get("screenshotPath")]
    rows=surface_rows+state_rows
    if not rows: raise ValueError("The native capture batch contains no staged screenshots.")
    tile_width,tile_height,label_height,columns=480,300,34,4
    image_height=tile_height-label_height
    row_count=(len(rows)+columns-1)//columns
    canvas=Image.new("RGB",(tile_width*columns,tile_height*row_count),(16,16,18))
    draw=ImageDraw.Draw(canvas); font=ImageFont.load_default()
    rendered=[]
    for index,item in enumerate(rows):
        screenshot=Path(item["screenshotPath"]).resolve()
        if not screenshot.is_file(): raise ValueError(f"Staged screenshot is missing: {screenshot}")
        with Image.open(screenshot) as source:
            source=source.convert("RGB"); source.thumbnail((tile_width,image_height),Image.Resampling.LANCZOS)
            x=(index%columns)*tile_width+(tile_width-source.width)//2
            y=(index//columns)*tile_height+(image_height-source.height)//2
            canvas.paste(source,(x,y))
        identity=item.get("stateId") if item.get("contactSheetKind")=="runtime-state" else item.get("targetId")
        label=f"{identity or 'unknown'}  {batch.get('resolution','')} @ {float(batch.get('uiScale',0)):.2f}"
        label_x=(index%columns)*tile_width+8; label_y=(index//columns)*tile_height+image_height+10
        draw.text((label_x,label_y),label,fill=(235,210,145),font=font)
        rendered.append({"kind":item.get("contactSheetKind","surface"),"targetId":item.get("targetId",item.get("parentTargetId","")),
                         "stateId":item.get("stateId",""),"screenshotPath":str(screenshot),"screenshotSha256":digest(screenshot)})
    output_path=output_path.resolve(); output_path.parent.mkdir(parents=True,exist_ok=True); canvas.save(output_path)
    report={"schema":"reign-ui-native-contact-sheet-v1","generatedUtc":datetime.now(timezone.utc).isoformat(),
      "batchReportPath":str(batch_report_path.resolve()),"outputPath":str(output_path),"outputSha256":digest(output_path),
      "resolution":batch.get("resolution",""),"uiScale":batch.get("uiScale",0),"surfaceCount":len(surface_rows),
      "stateCount":len(state_rows),"caseCount":len(rendered),"cases":rendered}
    output_path.with_suffix(output_path.suffix+".json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    return report

def dashboard(inventory_path,evidence_root,output):
    inv=json.loads(inventory_path.read_text(encoding="utf-8")); reports={}
    if evidence_root.exists():
        for path in evidence_root.rglob("fit-report.json"):
            relative=path.relative_to(evidence_root)
            if relative.parts: reports.setdefault(relative.parts[0],[]).append((path,json.loads(path.read_text(encoding="utf-8"))))
    required_cases=sum(len(case["uiScales"]) for case in inv["targetMatrix"])
    lines=["# Reign UI Calibration Dashboard","",f"Generated: {datetime.now(timezone.utc).isoformat()}","","| UI | Entry path | Installed | Stable IDs | Live state | Evidence |","|---|---|---|---:|---|---|"]
    for ui in inv["interfaces"]:
        ui_reports=reports.get(ui["id"],[]); evidence=ui_reports[-1][0].as_posix() if ui_reports else "—"
        installed="match" if ui.get("installedMatchesSource") else ("missing" if ui.get("installedExists") is False else "not-audited")
        if ui.get("supportUi"):
            evidence="provider-free browser render matrix"
            state="previewer-only; native acceptance not applicable"
        else:
            state=f"measured {len(ui_reports)}/{required_cases}" if ui_reports else ui["state"]
        lines.append(f"| {ui['movie']} | {ui['entry']} | {installed} | {ui['stableIdentityCoverage']:.0%} | {state} | {evidence} |")
    lines += ["","A runtime UI passes only when every required resolution/UI-scale case has a native screenshot, runtime snapshot, zero blocking binding/asset/input diagnostics, and reviewed fit evidence. Previewer-only support assets are not native acceptance surfaces."]
    output.parent.mkdir(parents=True,exist_ok=True); output.write_text("\n".join(lines)+"\n",encoding="utf-8")

def main():
    parser=argparse.ArgumentParser(); sub=parser.add_subparsers(dest="command",required=True)
    p=sub.add_parser("inventory"); p.add_argument("--output",type=Path,required=True); p.add_argument("--installed-root",type=Path)
    p=sub.add_parser("compare"); p.add_argument("--reference",type=Path,required=True); p.add_argument("--live",type=Path,required=True); p.add_argument("--output-dir",type=Path,required=True); p.add_argument("--snapshot",type=Path)
    p=sub.add_parser("dashboard"); p.add_argument("--inventory",type=Path,required=True); p.add_argument("--evidence-root",type=Path,required=True); p.add_argument("--output",type=Path,required=True)
    p=sub.add_parser("contact-sheet"); p.add_argument("--batch-report",type=Path,required=True); p.add_argument("--output",type=Path,required=True)
    p=sub.add_parser("accept-native-case"); p.add_argument("--target",required=True); p.add_argument("--resolution",required=True); p.add_argument("--ui-scale",type=float,required=True); p.add_argument("--screenshot",type=Path,required=True); p.add_argument("--capture-metadata",type=Path); p.add_argument("--snapshot",type=Path); p.add_argument("--evidence-root",type=Path,required=True); p.add_argument("--installed-root",type=Path); p.add_argument("--render-report",type=Path,required=True); p.add_argument("--diagnostic-count",type=int,required=True); p.add_argument("--reviewed",action="store_true"); p.add_argument("--review-confirmation",help=f"Exact reviewed-case attestation: {NATIVE_REVIEW_CONFIRMATION}")
    p=sub.add_parser("accept-native-state-case"); p.add_argument("--target",required=True); p.add_argument("--state",required=True); p.add_argument("--resolution",required=True); p.add_argument("--ui-scale",type=float,required=True); p.add_argument("--screenshot",type=Path,required=True); p.add_argument("--capture-metadata",type=Path); p.add_argument("--snapshot",type=Path,required=True); p.add_argument("--state-receipt",type=Path,required=True); p.add_argument("--evidence-root",type=Path,required=True); p.add_argument("--installed-root",type=Path,required=True); p.add_argument("--render-report",type=Path,required=True); p.add_argument("--diagnostic-count",type=int,required=True); p.add_argument("--reviewed",action="store_true"); p.add_argument("--review-confirmation",help=f"Exact reviewed-case attestation: {NATIVE_REVIEW_CONFIRMATION}")
    a=parser.parse_args()
    try:
        if a.command=="inventory": print(json.dumps(inventory(a.output,a.installed_root),indent=2))
        elif a.command=="compare": print(json.dumps(compare(a.reference,a.live,a.output_dir,a.snapshot),indent=2))
        elif a.command=="dashboard": dashboard(a.inventory,a.evidence_root,a.output)
        elif a.command=="contact-sheet": print(json.dumps(contact_sheet(a.batch_report,a.output),indent=2))
        elif a.command=="accept-native-case": print(json.dumps(record_acceptance(a.target,a.resolution,a.ui_scale,a.screenshot,a.capture_metadata,a.snapshot,a.evidence_root,a.installed_root,a.render_report,a.diagnostic_count,a.reviewed,a.review_confirmation),indent=2))
        else: print(json.dumps(record_runtime_state_acceptance(a.target,a.state,a.resolution,a.ui_scale,a.screenshot,a.capture_metadata,a.snapshot,a.state_receipt,a.evidence_root,a.installed_root,a.render_report,a.diagnostic_count,a.reviewed,a.review_confirmation),indent=2))
    except (ValueError,FileNotFoundError,json.JSONDecodeError) as error:
        parser.error(str(error))
if __name__=="__main__": main()
