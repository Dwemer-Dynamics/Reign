#!/usr/bin/env python3
"""Read one texture from a Bannerlord TPAC package without mutating the package.

This intentionally implements only the small, stable portion of TPAC needed for
texture discovery and extraction.  It is suitable for editor evidence workflows
where opening a packed texture in the Resource Browser is unsafe or loses source
provenance.  The script writes a PNG and a sidecar JSON audit containing package,
asset, metadata, segment, and output hashes.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import uuid
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import BinaryIO

from PIL import Image


TPAC_MAGIC = 0x43415054
TEXTURE_TYPE_GUID = "c974cbcb-5f1c-49f6-9a32-2b5b6c92c2e8"
TEXTURE_PIXEL_TYPE_GUID = "70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c"


def read_exact(stream: BinaryIO, count: int) -> bytes:
    data = stream.read(count)
    if len(data) != count:
        raise EOFError(f"Expected {count} bytes at offset {stream.tell() - len(data)}, got {len(data)}")
    return data


def read_u8(stream: BinaryIO) -> int:
    return read_exact(stream, 1)[0]


def read_u16(stream: BinaryIO) -> int:
    return struct.unpack("<H", read_exact(stream, 2))[0]


def read_u32(stream: BinaryIO) -> int:
    return struct.unpack("<I", read_exact(stream, 4))[0]


def read_i32(stream: BinaryIO) -> int:
    return struct.unpack("<i", read_exact(stream, 4))[0]


def read_u64(stream: BinaryIO) -> int:
    return struct.unpack("<Q", read_exact(stream, 8))[0]


def read_i64(stream: BinaryIO) -> int:
    return struct.unpack("<q", read_exact(stream, 8))[0]


def read_guid(stream: BinaryIO) -> str:
    return str(uuid.UUID(bytes_le=read_exact(stream, 16)))


def read_sized_string(stream: BinaryIO) -> str:
    length = read_i32(stream)
    if length < 0 or length > 64 * 1024 * 1024:
        raise ValueError(f"Invalid sized-string length {length} at offset {stream.tell() - 4}")
    return read_exact(stream, length).decode("utf-8", errors="replace")


def read_string_list(stream: BinaryIO) -> list[str]:
    count = read_i32(stream)
    if count < 0 or count > 1_000_000:
        raise ValueError(f"Invalid string-list count {count} at offset {stream.tell() - 4}")
    return [read_sized_string(stream) for _ in range(count)]


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def lz4_block_decompress(source: bytes, expected_size: int) -> bytes:
    """Decode a raw LZ4 block (the TPAC LZ4HC storage representation)."""
    target = bytearray()
    cursor = 0
    source_size = len(source)
    while cursor < source_size:
        token = source[cursor]
        cursor += 1

        literal_length = token >> 4
        if literal_length == 15:
            while True:
                extension = source[cursor]
                cursor += 1
                literal_length += extension
                if extension != 255:
                    break
        literal_end = cursor + literal_length
        if literal_end > source_size:
            raise ValueError("LZ4 literal run extends beyond source block")
        target.extend(source[cursor:literal_end])
        cursor = literal_end
        if cursor == source_size:
            break

        if cursor + 2 > source_size:
            raise ValueError("LZ4 match offset is truncated")
        match_offset = source[cursor] | (source[cursor + 1] << 8)
        cursor += 2
        if match_offset <= 0 or match_offset > len(target):
            raise ValueError(f"Invalid LZ4 match offset {match_offset}")

        match_length = token & 0x0F
        if match_length == 15:
            while True:
                extension = source[cursor]
                cursor += 1
                match_length += extension
                if extension != 255:
                    break
        match_length += 4
        match_start = len(target) - match_offset
        for index in range(match_length):
            target.append(target[match_start + index])

        if len(target) > expected_size:
            raise ValueError(
                f"LZ4 block expanded beyond declared size {expected_size}: {len(target)}"
            )

    if len(target) != expected_size:
        raise ValueError(f"LZ4 output size mismatch: expected {expected_size}, got {len(target)}")
    return bytes(target)


@dataclass(frozen=True)
class Segment:
    offset: int
    actual_size: int
    storage_size: int
    owner_guid: str
    type_guid: str
    unknown_u64: int
    unknown_u32: int
    storage_format: int


@dataclass(frozen=True)
class TextureMetadata:
    metadata_version: int
    billboard_material_guid: str
    source: str
    flags: list[str]
    width: int
    height: int
    mipmap_count: int
    array_count: int
    texture_format: str
    system_flags: list[str]
    generated_asset_count: int
    trailing_metadata_bytes: int


@dataclass(frozen=True)
class TextureAsset:
    record_index: int
    asset_type_guid: str
    asset_guid: str
    asset_version: int
    name: str
    metadata_size: int
    metadata: TextureMetadata
    metadata_checksum: int
    segments: list[Segment]
    dependency_count: int


def parse_texture_metadata(raw: bytes) -> TextureMetadata:
    from io import BytesIO

    stream = BytesIO(raw)
    metadata_version = read_u32(stream)
    billboard = read_guid(stream)
    read_u32(stream)
    source = read_sized_string(stream)
    read_u64(stream)
    read_u8(stream)
    read_u32(stream)
    flags = read_string_list(stream)
    unknown_u32_3 = read_u32(stream)
    read_u8(stream)
    width = read_u32(stream)
    height = read_u32(stream)
    read_u32(stream)
    mipmap_count = read_u8(stream)
    array_count = read_u16(stream)
    texture_format = read_sized_string(stream)
    read_u32(stream)
    system_flags = read_string_list(stream)
    if unknown_u32_3 > 0:
        read_u32(stream)
        read_u32(stream)

    generated_asset_count = read_u32(stream)
    if generated_asset_count > 1_000_000:
        raise ValueError(f"Invalid generated-asset count {generated_asset_count}")
    for _ in range(generated_asset_count):
        read_guid(stream)
        read_guid(stream)

    if metadata_version >= 2 and stream.tell() + 8 <= len(raw):
        read_u64(stream)
    if metadata_version >= 3 and stream.tell() + 32 <= len(raw):
        read_exact(stream, 32)

    return TextureMetadata(
        metadata_version=metadata_version,
        billboard_material_guid=billboard,
        source=source,
        flags=flags,
        width=width,
        height=height,
        mipmap_count=mipmap_count,
        array_count=array_count,
        texture_format=texture_format,
        system_flags=system_flags,
        generated_asset_count=generated_asset_count,
        trailing_metadata_bytes=len(raw) - stream.tell(),
    )


def find_texture(package_path: Path, asset_name: str) -> tuple[dict[str, object], TextureAsset]:
    with package_path.open("rb") as stream:
        magic = read_u32(stream)
        if magic != TPAC_MAGIC:
            raise ValueError(f"Not a TPAC package: magic 0x{magic:08X}")
        package_version = read_u32(stream)
        if package_version not in (1, 2):
            raise ValueError(f"Unsupported TPAC package version {package_version}")
        package_guid = read_guid(stream)
        resource_count = read_u32(stream)
        data_offset = read_u32(stream)
        reserved = read_u32(stream)

        matches: list[TextureAsset] = []
        available_textures: list[str] = []
        for record_index in range(resource_count):
            asset_type_guid = read_guid(stream)
            asset_guid = read_guid(stream)
            asset_version = read_u32(stream) if package_version > 1 else 0
            name = read_sized_string(stream)
            metadata_size = read_u64(stream)
            metadata_raw = read_exact(stream, metadata_size)
            metadata_checksum = read_i64(stream)
            segment_count = read_i32(stream)
            if segment_count < 0 or segment_count > 1_000_000:
                raise ValueError(f"Invalid segment count {segment_count} for asset {name}")
            segments = [
                Segment(
                    offset=read_u64(stream),
                    actual_size=read_u64(stream),
                    storage_size=read_u64(stream),
                    owner_guid=read_guid(stream),
                    type_guid=read_guid(stream),
                    unknown_u64=read_u64(stream),
                    unknown_u32=read_u32(stream),
                    storage_format=read_u8(stream),
                )
                for _ in range(segment_count)
            ]
            dependency_count = read_i32(stream)
            if dependency_count < 0 or dependency_count > 1_000_000:
                raise ValueError(f"Invalid dependency count {dependency_count} for asset {name}")
            read_exact(stream, dependency_count * 48)

            if asset_type_guid.lower() == TEXTURE_TYPE_GUID:
                available_textures.append(name)
                if name.casefold() == asset_name.casefold():
                    matches.append(
                        TextureAsset(
                            record_index=record_index,
                            asset_type_guid=asset_type_guid,
                            asset_guid=asset_guid,
                            asset_version=asset_version,
                            name=name,
                            metadata_size=metadata_size,
                            metadata=parse_texture_metadata(metadata_raw),
                            metadata_checksum=metadata_checksum,
                            segments=segments,
                            dependency_count=dependency_count,
                        )
                    )

        if not matches:
            similar = [name for name in available_textures if asset_name.casefold() in name.casefold()]
            raise KeyError(
                f"Texture {asset_name!r} was not found. Similar packed textures: {similar[:25]}"
            )
        if len(matches) != 1:
            raise ValueError(f"Texture {asset_name!r} matched {len(matches)} records")

        header = {
            "magic": f"0x{magic:08X}",
            "packageVersion": package_version,
            "packageGuid": package_guid,
            "resourceCount": resource_count,
            "declaredDataOffset": data_offset,
            "reserved": reserved,
            "metadataEndOffset": stream.tell(),
        }
        return header, matches[0]


def read_segment(package_path: Path, segment: Segment) -> bytes:
    with package_path.open("rb") as stream:
        stream.seek(segment.offset)
        stored = read_exact(stream, segment.storage_size)
    if segment.storage_format == 0:
        raw = stored
    elif segment.storage_format == 1:
        raw = lz4_block_decompress(stored, segment.actual_size)
    else:
        raise ValueError(f"Unsupported TPAC storage format {segment.storage_format}")
    if len(raw) != segment.actual_size:
        raise ValueError(
            f"Segment size mismatch: expected {segment.actual_size}, decoded {len(raw)}"
        )
    return raw


def texture_image(metadata: TextureMetadata, raw: bytes) -> Image.Image:
    format_to_mode = {
        "R8G8B8A8_UNORM": "RGBA",
        "B8G8R8A8_UNORM": "BGRA",
        "B8G8R8X8_UNORM": "BGRX",
        "R8_UNORM": "L",
        "L8_UNORM": "L",
        "A8_UNORM": "L",
        "R16_UNORM": "I;16",
        "L16_UNORM": "I;16",
    }
    raw_mode = format_to_mode.get(metadata.texture_format)
    if raw_mode is None:
        raise ValueError(
            f"Texture format {metadata.texture_format!r} is not yet supported by the PNG exporter"
        )
    if raw_mode in ("RGBA", "BGRA", "BGRX"):
        bytes_per_pixel = 4
    elif raw_mode == "I;16":
        bytes_per_pixel = 2
    else:
        bytes_per_pixel = 1
    primary_size = metadata.width * metadata.height * bytes_per_pixel
    if len(raw) < primary_size:
        raise ValueError(
            f"Pixel segment is too small for primary image: need {primary_size}, got {len(raw)}"
        )
    primary = raw[:primary_size]
    if raw_mode == "RGBA":
        return Image.frombytes("RGBA", (metadata.width, metadata.height), primary)
    if raw_mode in ("BGRA", "BGRX"):
        return Image.frombytes("RGBA", (metadata.width, metadata.height), primary, "raw", raw_mode)
    if raw_mode == "I;16":
        return Image.frombytes("I;16", (metadata.width, metadata.height), primary)
    return Image.frombytes("L", (metadata.width, metadata.height), primary)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path, help="Path to the source .tpac package")
    parser.add_argument("asset", help="Exact packed texture asset name")
    parser.add_argument("output", type=Path, help="Destination PNG path")
    parser.add_argument(
        "--audit",
        type=Path,
        help="Destination audit JSON path (defaults beside the PNG)",
    )
    args = parser.parse_args()

    package_path = args.package.resolve()
    output_path = args.output.resolve()
    audit_path = (args.audit or output_path.with_suffix(".audit.json")).resolve()
    if not package_path.is_file():
        raise FileNotFoundError(package_path)

    package_header, asset = find_texture(package_path, args.asset)
    pixel_segments = [
        segment for segment in asset.segments if segment.type_guid.lower() == TEXTURE_PIXEL_TYPE_GUID
    ]
    if len(pixel_segments) != 1:
        raise ValueError(
            f"Expected one texture-pixel segment for {asset.name}, found {len(pixel_segments)}"
        )

    pixel_raw = read_segment(package_path, pixel_segments[0])
    image = texture_image(asset.metadata, pixel_raw)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path, format="PNG", optimize=True)

    audit = {
        "schema": "bannerlord-tpac-texture-extraction-v1",
        "readOnly": True,
        "package": {
            "path": str(package_path),
            "sizeBytes": package_path.stat().st_size,
            "sha256": sha256_file(package_path),
            **package_header,
        },
        "asset": {
            **asdict(asset),
            "pixelSegmentDecodedSha256": hashlib.sha256(pixel_raw).hexdigest().upper(),
        },
        "output": {
            "path": str(output_path),
            "mode": image.mode,
            "width": image.width,
            "height": image.height,
            "sizeBytes": output_path.stat().st_size,
            "sha256": sha256_file(output_path),
        },
    }
    audit_path.parent.mkdir(parents=True, exist_ok=True)
    audit_path.write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(audit, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
