#!/usr/bin/env python3
"""Package reviewed text-generated Nord portraits without changing image bytes.

Example (Python 3.10+, no third-party packages):
  python Package-NordPortraits.py --evidence-root E:/.../NordCourt/<task>

The evidence root must contain the four worker directories, native-sources,
and baseline/ReignBeta/PortraitCache/shared-portrait-inventory.json. Use
--allow-partial while workers finish: accepted folders are copied, but the
canonical inventory is published only when all 78 portraits pass. Existing
different files are always an error. Reruns verify rather than overwrite.
No generation, provider calls, native game, database, or build is performed.
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import shutil
import struct
import sys
import uuid
import zlib

TICKS_EPOCH = 621355968000000000
FILES = (
    'portrait.png', 'portrait_chest.png', 'thumbnail.png', 'thumbnail_wide.png',
    'zoom.png', '.portrait_derivatives.json', 'formal_outfit.json',
    'portrait_input.json', 'prompt.txt', '.ai_generation.json',
)
ASSEMBLY_SHA = '000bb48a0c7ee622e05c053a58a4bf7db836761e50f8710902ed3aca9665f356'
MODEL_SHA = '34cd7e60aeff28744c657de7a3dc64e872d506741de66987f3426f2b79f88017'
ACCEPTED = {'accepted', 'accepted_master_and_derivatives', 'accepted_with_production_derivatives'}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def record(path, relative):
    stat = path.stat()
    return dict(path=relative, bytes=stat.st_size, sha256=sha(path),
                lastWriteUtcTicks=stat.st_mtime_ns // 100 + TICKS_EPOCH)


def png_size(path):
    """Validate PNG chunk checksums and compressed pixels, without re-encoding."""
    data = path.read_bytes()
    require(data[:8] == b'\x89PNG\r\n\x1a\n', f'Not PNG: {path}')
    offset, size, compressed, ended = 8, None, [], False
    while offset < len(data):
        require(offset + 12 <= len(data), f'Truncated PNG: {path}')
        length = struct.unpack_from('>I', data, offset)[0]
        kind = data[offset + 4:offset + 8]
        body = data[offset + 8:offset + 8 + length]
        require(offset + length + 12 <= len(data), f'Truncated PNG chunk: {path}')
        checksum = struct.unpack_from('>I', data, offset + 8 + length)[0]
        require(zlib.crc32(kind + body) & 0xffffffff == checksum, f'PNG CRC: {path}')
        if kind == b'IHDR':
            width, height, depth, color, comp, filt, interlace = struct.unpack('>IIBBBBB', body)
            require(depth == 8 and color in (2, 6) and comp == filt == interlace == 0,
                    f'Unsupported PNG format: {path}')
            size = (width, height)
            channels = 3 if color == 2 else 4
        elif kind == b'IDAT':
            compressed.append(body)
        elif kind == b'IEND':
            ended = True
        offset += length + 12
    require(ended and size and compressed, f'Incomplete PNG: {path}')
    pixels = zlib.decompress(b''.join(compressed))
    require(len(pixels) == (size[0] * channels + 1) * size[1], f'PNG pixel length: {path}')
    return size


def write_once(path, data):
    if path.exists():
        require(path.read_bytes() == data, f'Refusing to overwrite different file: {path}')
    else:
        with path.open('xb') as stream:
            stream.write(data)


def copy_once(source, target):
    before = record(source, target.name)
    if target.exists():
        after = record(target, target.name)
        require(before == after, f'Refusing to overwrite different bytes or timestamp: {target}')
    else:
        shutil.copy2(source, target)
        require(record(target, target.name) == before, f'Copy bytes/ticks mismatch: {target}')


def accepted_ids(root, worker):
    """C explicitly publishes a rolling accepted whitelist; other receipts are final gates."""
    if worker != 'portraits-c':
        return None
    path = root / worker / 'inventory.json'
    if not path.exists():
        return set()
    return {item['heroStringId'] for item in read_json(path)['rows']}


def validate_product(row, folder, native_folder):
    generation = read_json(folder / 'generation-receipt.json')
    final = read_json(folder / 'finalization-receipt.json')
    native = read_json(native_folder / 'portrait_input.json')
    derivative = read_json(folder / '.portrait_derivatives.json')
    outfit = read_json(folder / 'formal_outfit.json')
    key = row['name'].replace(' ', '_') + ' (' + row['id'] + ')'
    for data in (generation, native):
        require(data['heroStringId'] == row['id'] and data['cacheKey'] == key
                and data['characterName'] == row['name'], f'Identity mismatch: {row["id"]}')
    require(generation['status'] in ACCEPTED, f'Unaccepted master: {row["id"]}')
    require(generation['provider'] == 'Codex built-in image_gen'
            and generation['generationMethod'] == 'text_only'
            and generation['imageReferencesSent'] == 0, f'Generation route: {key}')
    require(native['cultureId'] == 'nord' and native['clanTier'] == row['clan_tier'] == 1
            and native['socialStation'] == 'landowner', f'Culture/rank: {key}')
    require(native['bodyWeight'] == generation['nativeWeight'] == 0.5
            and native['bodyBuild'] == generation['nativeBuild'] == 0.5
            and native['bodyKey'] == generation['nativeBodyKey']
            and native['ageYears'] == generation['ageYears'] == row['age'], f'Physique: {key}')
    source_sha = sha(native_folder / 'source.png')
    require(source_sha == native['sourceSha256'] == generation['sourceSha256'], f'Native source SHA: {key}')
    require(native['physique']['sourceSha256'] == source_sha, f'Physique source binding: {key}')
    if 'physicalConfidence' in generation:
        require(generation['physicalConfidence'] == native['physicalConfidence'], f'Profile confidence binding: {key}')
    master = folder / 'portrait.png'
    master_sha = sha(master)
    require(master_sha == generation['portraitSha256'] == final['masterSha256'], f'Master SHA: {key}')
    require(master_sha == sha(Path(generation['generatedOriginalPath'])), f'Generated original SHA: {key}')
    dimensions = png_size(master)
    require(dimensions == (generation['portraitWidth'], generation['portraitHeight'])
            == (final['width'], final['height']) == (1086, 1448), f'Master dimensions: {key}')
    require(master.stat().st_size == generation['portraitBytes'] == derivative['SourceLength'], f'Master bytes: {key}')
    require(master.stat().st_mtime_ns // 100 + TICKS_EPOCH == derivative['SourceLastWriteUtcTicks'], f'Master ticks: {key}')
    require(final['schema'] == 'reign-nord-portrait-finalization-v1'
            and final['productionDetector'] is True and final['productionCropBuilder'] is True
            and final['databaseAccess'] is False and final['masterModified'] is False
            and final['derivativesWritten'] is True, f'Production finalization: {key}')
    require(final['artifactAssemblySha256'] == ASSEMBLY_SHA and final['faceModelSha256'] == MODEL_SHA,
            f'Unreviewed artifact/model: {key}')
    focus = final['focus']
    require(focus['Method'] == 'ultraface' and focus['Model'] == 'version-RFB-320'
            and focus['CandidateCount'] == 1 and .08 <= focus['Height'] <= .18
            and .035 <= focus['Y'] <= .18 and .35 <= focus['X'] + focus['Width'] / 2 <= .65,
            f'Actual face/composition gate: {key}')
    require(derivative == final['derivatives'] and derivative['Version'] == 3
            and derivative['CropAlgorithmVersion'] == 2 and derivative['NeedsReview'] is False
            and derivative['FaceCandidateCount'] == 1, f'Derivative manifest: {key}')
    require(all(derivative['Face' + field] == focus[field] for field in ('X', 'Y', 'Width', 'Height'))
            and derivative['FocusConfidence'] == focus['Confidence'], f'Actual detector metadata: {key}')
    require(final['reviewedHeadroomFaceHeights'] in (.55, .65)
            and derivative['CropY'] == derivative['WideCropY'] == final['reviewedCropTop'], f'Reviewed crop: {key}')
    for name, width, height in (
        ('portrait_chest.png', 'PortraitWidth', 'PortraitHeight'),
        ('thumbnail.png', 'ThumbnailWidth', 'ThumbnailHeight'),
        ('thumbnail_wide.png', 'PartyThumbnailWidth', 'PartyThumbnailHeight'),
        ('zoom.png', 'ZoomWidth', 'ZoomHeight'),
    ):
        require(png_size(folder / name) == (derivative[width], derivative[height]), f'Derivative dimensions: {key}/{name}')
    review = generation['review']
    worker_review = None
    worker_report = folder.parent / 'worker-a-report.json'
    if worker_report.exists():
        worker_review = next((item for item in read_json(worker_report)['records']
                              if item['heroStringId'] == row['id']), None)
        require(worker_review and worker_review['masterSha256'] == master_sha
                and worker_review['reviewPassed'] is True, f'Worker acceptance: {key}')
    require(review['sourceInspected'] is True and review['oneSubject'] is True
            and review['culturalFacialReferenceApplied'] is True
            and (review.get('fullBodyHeadAndFeetVisible') is True or
                 review.get('fullBody') is True and review.get('headAndFeetVisible') is True), f'Master visual review: {key}')
    require(review.get('allFourDerivativesInspected') is True or
            review.get('normalAndWideAndChestCropsInspected') is True or
            review.get('productionDerivativesInspected') == ['portrait_chest.png', 'thumbnail_wide.png'],
            f'Derivative visual review: {key}')
    require(review['reviewedHeadroomFaceHeights'] == final['reviewedHeadroomFaceHeights'], f'Crop review mismatch: {key}')
    require(generation.get('adultClothingEditPerformed', False) is False
            and (generation.get('normalPortrait') is True or review.get('normalPortraitOnly') is True
                 or worker_review and worker_review.get('normalPortrait') is True), f'Normal portrait review: {key}')
    require(outfit['heroStringId'] == row['id'] and outfit['imageSha256'].lower() == master_sha
            and outfit['status'] == 'completed' and outfit['description'], f'Formal outfit binding: {key}')
    prompt_sha = sha(folder / 'prompt.txt')
    # Some worker receipts hashed Python's universal-newline text, rather than
    # on-disk CRLF bytes. Verify that declared value without changing the prompt.
    normalized_prompt_sha = hashlib.sha256((folder / 'prompt.txt').read_text(encoding='utf-8').encode('utf-8')).hexdigest()
    declared_prompt_sha = generation.get('promptFileSha256', generation['promptSha256'])
    require(declared_prompt_sha in (prompt_sha, normalized_prompt_sha), f'Prompt SHA: {key}')
    prompt_hash_basis = ('exact UTF-8 file bytes' if declared_prompt_sha == prompt_sha
                         else 'UTF-8 file text with universal newlines normalized to LF')
    if 'requestTextFile' in generation:
        request_file = folder / generation['requestTextFile']
        require(request_file.parent == folder and sha(request_file) == generation['promptSha256'],
                f'Actual request text SHA: {key}')
    if 'submittedPromptUtf8Sha256' in generation:
        require(generation['submittedPromptUtf8Sha256'] == normalized_prompt_sha,
                f'Submitted LF request SHA: {key}')
    execution = generation['imageGenerationExecutionId']
    require(str(uuid.UUID(execution)) == execution and execution in generation['generatedOriginalPath'], f'Execution UUID: {key}')
    adapter = dict(
        version=1, schema='reign-nord-builtin-text-generation-v1', cacheKey=key,
        heroStringId=row['id'], characterObjectId=native['characterObjectId'], characterName=row['name'],
        provider=generation['provider'], model=generation['model'],
        adapter='native-vision-description/text-only-image-generation', generationMethod='text_only', inputImages=0,
        imageGenerationExecutionId=execution, providerApiRequestId=None,
        providerApiRequestIdAvailability='not_exposed_by_builtin_tool',
        sourceSha256=source_sha, portraitSha256=master_sha, portraitWidth=dimensions[0], portraitHeight=dimensions[1],
        promptSha256=prompt_sha, promptFileSha256=prompt_sha,
        workerPromptHashDefinition=prompt_hash_basis, physicalConfidence=native['physicalConfidence'],
        cultureId='nord', cultureReference='Scandinavian regional appearance, drawing on Norwegian, Swedish and Danish populations.',
        normalPortrait=True, adultClothingEditPerformed=False, derivativesReady=True, derivativeError='',
        generatedUtc=generation['completedUtc'], serverSigned=False,
        nativeInputFileSha256=sha(native_folder / 'portrait_input.json'),
        nativeInputHash=native['inputHash'], nativeCatalogHash=native['catalogHash'],
        nativeRenderContractHash=native['renderContractHash'],
        nativeBatchProvenance=native['nativeBatchProvenance'],
        generationReceipt=generation, finalizationReceipt=final,
    )
    if worker_review is not None:
        adapter['workerAcceptanceRecord'] = worker_review
        adapter['workerAcceptanceReportSha256'] = sha(worker_report)
    if 'profileCorrectionProvenance' in native:
        adapter['profileCorrectionProvenance'] = native['profileCorrectionProvenance']
    return key, adapter


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence-root', required=True, type=Path)
    parser.add_argument('--workspace', type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument('--allow-partial', action='store_true')
    parser.add_argument('--hold', action='append', default=[], metavar='HERO_ID',
                        help='Exclude an identity pending upstream review (repeatable).')
    parser.add_argument('--reviewed-provenance-update', action='append', default=[], metavar='HERO_ID',
                        help='Allow only reviewed prompt-hash evidence additions to a previously packaged adapter; images remain immutable.')
    args = parser.parse_args()
    root, workspace = args.evidence_root.resolve(), args.workspace.resolve()
    durable = root / 'portrait-package'
    durable.mkdir(exist_ok=True)
    shared = workspace / 'ReignBeta/PortraitCache/_shared'
    inventory_path = shared.parent / 'shared-portrait-inventory.json'
    baseline_path = root / 'baseline/ReignBeta/PortraitCache/shared-portrait-inventory.json'
    baseline = read_json(baseline_path)
    require(baseline['schema'] == 'reign-shared-content-inventory-v1' and baseline['root'] == '_shared', 'Baseline schema')
    rows = read_json(workspace / 'ReignBeta/ModuleData/reign_nord_households.json')['records']
    require(len(rows) == 78 and len({row['id'] for row in rows}) == 78, 'Expected 78 distinct manifest identities')
    require(set(args.hold) <= {row['id'] for row in rows}, 'Unknown held identity')
    require(set(args.reviewed_provenance_update) <= {row['id'] for row in rows}, 'Unknown provenance-update identity')
    old_entries = {entry['path']: entry for entry in baseline['files']}
    require(len(old_entries) == len(baseline['files']), 'Duplicate baseline paths')
    require(len({Path(path).parts[0] for path in old_entries}) == 1228, 'Expected 1228 existing portrait folders')
    current = read_json(inventory_path)
    current_entries = {entry['path']: entry for entry in current['files']}
    require(all(current_entries.get(path) == entry for path, entry in old_entries.items()), 'Existing inventory entry was changed')
    previous_receipt = durable / 'package-progress.json'
    prior_files = {entry['path']: entry for entry in read_json(previous_receipt)['files']} if previous_receipt.exists() else {}
    # Audit each old byte before any package mutation. Exact observed timestamps are
    # retained separately because older inventory timestamps can predate hydration.
    def audit_old(entry):
        actual = record(shared / entry['path'], entry['path'])
        require(actual['bytes'] == entry['bytes'] and actual['sha256'] == entry['sha256'], f'Old asset mismatch: {entry["path"]}')
        return actual
    with ThreadPoolExecutor(max_workers=4) as pool:
        old_actual = list(pool.map(audit_old, baseline['files']))
    old_audit_path = durable / 'old-assets-before.json'
    write_once(old_audit_path, json_bytes(dict(schema='reign-old-portrait-preservation-v1', files=old_actual)))
    c_ready = accepted_ids(root, 'portraits-c')
    products, pending = [], []
    for index, row in enumerate(rows):
        worker = ('portraits-a' if index < 20 else 'portraits-b' if index < 40 else 'portraits-c' if index < 60 else 'portraits-root')
        folder = root / worker / row['id']
        receipt = folder / 'generation-receipt.json'
        if (row['id'] in args.hold or worker == 'portraits-c' and row['id'] not in c_ready or not receipt.exists()
                or read_json(receipt).get('status') not in ACCEPTED):
            pending.append(row['id'])
            continue
        native_folder = root / 'native-sources/_shared' / (row['name'].replace(' ', '_') + ' (' + row['id'] + ')')
        key, adapter = validate_product(row, folder, native_folder)
        products.append((row, folder, native_folder, key, adapter))
    require(args.allow_partial or not pending, f'Incomplete: {len(pending)} portraits remain; use --allow-partial to copy accepted products only')
    # Validate every ready product first. Never publish a partial canonical inventory.
    packaged = []
    for row, folder, native_folder, key, adapter in products:
        require(not any(path.startswith(key + '/') for path in old_entries), f'Old-folder collision: {key}')
        target = shared / key
        target.mkdir(exist_ok=True)
        require({p.name for p in target.iterdir()} <= set(FILES), f'Unexpected files in new folder: {target}')
        for name in FILES:
            destination = target / name
            if name == '.ai_generation.json':
                encoded = json_bytes(adapter)
                if destination.exists() and destination.read_bytes() != encoded and row['id'] in args.reviewed_provenance_update:
                    old = read_json(destination)
                    relative = key + '/' + name
                    require(record(destination, relative) == prior_files.get(relative), f'Unrecorded existing adapter: {key}')
                    require({k: v for k, v in old.items() if k not in ('generationReceipt', 'workerPromptHashDefinition')}
                            == {k: v for k, v in adapter.items() if k not in ('generationReceipt', 'workerPromptHashDefinition')},
                            f'Provenance update changes image/native/finalization fields: {key}')
                    allowed = {'promptSha256', 'promptSha256Definition', 'promptFileSha256', 'requestTextFile',
                               'initialRecordedPromptSha256', 'initialRecordedPromptSha256Definition',
                               'requestTextEncodingObservation', 'submittedPromptUtf8Sha256'}
                    old_generation = old['generationReceipt']
                    new_generation = adapter['generationReceipt']
                    require({k: v for k, v in old_generation.items() if k not in allowed}
                            == {k: v for k, v in new_generation.items() if k not in allowed},
                            f'Provenance update changes generation/review evidence: {key}')
                    archive = durable / 'reviewed-provenance-updates' / row['id'] / sha(destination)
                    archive.mkdir(parents=True, exist_ok=True)
                    copy_once(destination, archive / 'previous.ai_generation.json')
                    write_once(archive / 'review.json', json_bytes(dict(
                        reason='Reviewed worker clarification of authored prompt-file hashes, retained request-text hashes, availability and encoding. PNG/native/finalization data unchanged.',
                        previousSha256=sha(destination), replacementSha256=hashlib.sha256(encoded).hexdigest(),
                        generationReceiptChangedFields=sorted(k for k in old_generation.keys() | new_generation.keys()
                                                             if old_generation.get(k) != new_generation.get(k)),
                        imageBytesUnchanged=True)))
                    destination.write_bytes(encoded)
                else:
                    write_once(destination, encoded)
            else:
                copy_once((native_folder if name == 'portrait_input.json' else folder) / name, destination)
        require(read_json(folder / 'generation-receipt.json') == adapter['generationReceipt']
                and read_json(folder / 'finalization-receipt.json') == adapter['finalizationReceipt']
                and sha(target / 'portrait_input.json') == adapter['nativeInputFileSha256'],
                f'Input evidence changed during packaging: {key}')
        entries = [record(target / name, key + '/' + name) for name in FILES]
        packaged.extend(entries)
        require(read_json(target / '.portrait_derivatives.json')['SourceLastWriteUtcTicks']
                == (target / 'portrait.png').stat().st_mtime_ns // 100 + TICKS_EPOCH, f'Copied master currentness: {key}')
    old_after = [record(shared / entry['path'], entry['path']) for entry in old_actual]
    require(old_actual == old_after, 'Old portrait asset changed during packaging')
    # The original inventory uses case-insensitive path order.
    final_entries = sorted(baseline['files'] + packaged, key=lambda entry: entry['path'].casefold())
    expected_existing = set(old_entries) | {entry['path'] for entry in packaged}
    require(set(current_entries) <= expected_existing, 'Unexpected existing inventory additions')
    if not pending:
        require(len(packaged) == 780, 'Expected 780 new files')
        inventory = dict(baseline, files=final_entries)
        data = json_bytes(inventory)
        if inventory_path.read_bytes() != data:
            require(inventory_path.read_bytes() == baseline_path.read_bytes(), 'Refusing to overwrite different final inventory')
            inventory_path.write_bytes(data)
        require(len({p.name for p in shared.iterdir() if p.is_dir()}) == 1306, 'Expected 1306 shared portrait folders')
    else:
        require(inventory_path.read_bytes() == baseline_path.read_bytes(), 'Partial packaging must leave inventory unchanged')
    receipt = dict(schema='reign-nord-portrait-package-v1', complete=not pending,
                   acceptedPortraits=len(products), pending=pending, newFiles=len(packaged),
                   baselineInventorySha256=sha(baseline_path), inventorySha256=sha(inventory_path),
                   oldFilesPreserved=len(old_actual), oldBytesPreserved=sum(e['bytes'] for e in old_actual),
                   oldObservedTicksPreserved=True, sourceCopyBytesAndTicksPreserved=True,
                   oldAuditSha256=sha(old_audit_path), files=packaged)
    receipt_path = durable / ('package-final.json' if not pending else 'package-progress.json')
    receipt_path.write_bytes(json_bytes(receipt))
    print(json.dumps({k: v for k, v in receipt.items() if k != 'files'}))


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, KeyError, zlib.error) as error:
        print(f'PACKAGING FAILED: {error}', file=sys.stderr)
        sys.exit(1)
