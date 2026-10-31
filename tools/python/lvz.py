#!/usr/bin/env python3
# Copyright (c) 2025-2026 Mengal SRL. MIT license.
"""
Reference reader of the LapViz telemetry format, versions 3 and 2 (docs/lapviz-format.md), in plain Python: no
dependency, so that the format can be checked against something else than the .NET library.

    from lvz import read
    session = read("session.lvz")
    session.channels["GPS Speed"]   # list of floats of the first group (None: missing value)
    session.time                    # seconds since session.start, of the first group
    session.groups["engine"].channels["RPM"]  # the other groups, each with its own time
    session.laps()                  # laps of the preferred timing source
    session.events()                # markers, beacons, flags

Command line: python3 lvz.py file.lvz [...] prints a summary of each file and exits with 1 if one is invalid.
"""

import csv
import hashlib
import io
import json
import math
import re
import struct
import sys
import zipfile
from dataclasses import dataclass, field

SUPPORTED_VERSIONS = (2, 3)
_TYPES = {
    "float64": (8, "d"), "float32": (4, "f"),
    "int8": (1, "b"), "uint8": (1, "B"), "int16": (2, "h"), "uint16": (2, "H"), "int32": (4, "i"),
}
_MISSING = {"int8": -128, "uint8": 255, "int16": -32768, "uint16": 65535, "int32": -2147483648}  # Spec 3.1
_GROUP_NAME = re.compile(r"^[a-z0-9][a-z0-9_-]{0,63}$")


class LapVizFormatError(ValueError):
    """The file is not a valid LapViz file of a supported version."""


@dataclass
class LapVizGroup:
    name: str
    time: list = field(default_factory=list)  # Seconds since session.start, strictly increasing
    channels: dict = field(default_factory=dict)  # Name -> list of float or None


@dataclass
class LapVizSession:
    manifest: dict
    groups: dict = field(default_factory=dict)  # Name -> LapVizGroup, in the order of the file

    @property
    def time(self):
        """Time of the first group: the whole file in version 2."""
        return next(iter(self.groups.values())).time if self.groups else []

    @property
    def channels(self):
        """Channels of the first group: the whole file in version 2."""
        return next(iter(self.groups.values())).channels if self.groups else {}

    @property
    def start(self) -> str:
        return self.manifest["session"]["start"]

    def timing(self):
        """The preferred timing source: the first one measured by the logger, else the first one not declared, else the first one (spec 2.4)."""
        sources = self.manifest.get("timing", [])
        return next((s for s in sources if s.get("device")), next((s for s in sources if not s.get("declared")), sources[0] if sources else None))

    def laps(self):
        source = self.timing()
        return source["laps"] if source else []

    def events(self):
        """The events with a time and a type, ordered by time (spec 2.5)."""
        return sorted((e for e in self.manifest.get("events", []) if isinstance(e.get("time"), (int, float)) and e.get("type")), key=lambda e: e["time"])

    def best_lap(self):
        """The fastest complete lap after the out lap, neither invalid nor neutralized (spec 2.4)."""
        excluded = {"invalid", "neutralized"}
        complete = [lap for lap in self.laps() if lap.get("duration") and lap["number"] > 0 and not excluded & set(lap.get("flags", []))]
        return min(complete, key=lambda lap: lap["duration"], default=None)


def read(path_or_stream) -> LapVizSession:
    with zipfile.ZipFile(path_or_stream) as archive:
        names = set(archive.namelist())
        if "lapviz.json" not in names:
            raise LapVizFormatError("Not a LapViz version 2 file: no lapviz.json entry")
        manifest = json.loads(archive.read("lapviz.json").decode("utf-8"))
        if manifest.get("format") != "lapviz":
            raise LapVizFormatError("format is not 'lapviz'")
        version = manifest.get("version")
        if version not in SUPPORTED_VERSIONS:
            raise LapVizFormatError(f"Unsupported version {version!r}")
        if "start" not in manifest.get("session", {}):
            raise LapVizFormatError("session.start is missing")

        if version == 2:
            # Spec 6.1: one group, its channels and samples at the root, float channels only
            groups = [{"name": "main", "channels": manifest.get("channels", []), "samples": manifest.get("samples")}]
            if any(c.get("type", "float64") not in ("float32", "float64") for c in groups[0]["channels"]):
                raise LapVizFormatError("Version 2 has float channels only")
        else:
            groups = manifest.get("groups") or []
            if not groups:
                raise LapVizFormatError("groups is missing")

        session = LapVizSession(manifest)
        all_names = set()
        for spec in groups:
            name = spec.get("name", "")
            if not _GROUP_NAME.match(name) or name in session.groups:
                raise LapVizFormatError(f"Invalid or duplicate group name {name!r}")
            channels = spec.get("channels", [])
            for channel in channels:
                if channel.get("type", "float64") not in _TYPES:
                    raise LapVizFormatError(f"Unknown type {channel.get('type')!r} of channel {channel.get('name')!r}")
                if channel["name"].lower() in all_names:
                    raise LapVizFormatError(f"Duplicate channel {channel['name']!r}")
                all_names.add(channel["name"].lower())

            group = session.groups[name] = LapVizGroup(name)
            samples = spec.get("samples")
            if samples:
                if samples["file"] not in names:
                    raise LapVizFormatError(f"Entry {samples['file']} is missing")
                data = archive.read(samples["file"])
                if "sha256" in samples and hashlib.sha256(data).hexdigest() != samples["sha256"].lower():
                    raise LapVizFormatError(f"{samples['file']} is damaged: its SHA-256 is not the one of the manifest")
                if samples["encoding"] == "binary":
                    _read_binary(group, channels, samples, data)
                elif samples["encoding"] == "csv":
                    _read_csv(group, channels, data)
                else:
                    raise LapVizFormatError(f"Unknown encoding {samples['encoding']!r}")

            if any(b <= a for a, b in zip(group.time, group.time[1:])):
                raise LapVizFormatError(f"The times of group {name} are not strictly increasing")
        return session


def _unshuffle(column: bytes, count: int, size: int) -> bytes:
    """Spec 3.2: byte j of value i is at offset j * count + i."""
    out = bytearray(len(column))
    for j in range(size):
        out[j::size] = column[j * count:(j + 1) * count]
    return bytes(out)


def _read_binary(group, channels, samples, data):
    count = samples["count"]
    shuffle = samples.get("byteShuffle", False)
    sizes = [_TYPES[c.get("type", "float64")] for c in channels]
    expected = count * (8 + sum(size for size, _ in sizes))
    if len(data) != expected:
        raise LapVizFormatError(f"{samples['file']} has {len(data)} bytes, {expected} expected")

    def column(offset, size):
        raw = data[offset:offset + count * size]
        return _unshuffle(raw, count, size) if shuffle else raw

    offset = 0
    group.time = [microseconds / 1e6 for microseconds in struct.unpack(f"<{count}q", column(0, 8))]
    offset += count * 8
    for channel, (size, code) in zip(channels, sizes):
        values = struct.unpack(f"<{count}{code}", column(offset, size))
        missing = _MISSING.get(channel.get("type", "float64"))
        if missing is None:
            group.channels[channel["name"]] = [None if math.isnan(v) else v for v in values]
        else:
            # Spec 3.1: value = integer * scale + offset
            scale, shift = channel.get("scale", 1), channel.get("offset", 0)
            group.channels[channel["name"]] = [None if v == missing else v * scale + shift for v in values]
        offset += count * size


def _read_csv(group, channels, data):
    rows = list(csv.reader(io.StringIO(data.decode("utf-8-sig"))))
    header, rows = rows[0], [row for row in rows[1:] if row]
    names = [c["name"] for c in channels]
    if header != ["time"] + names:
        raise LapVizFormatError(f"CSV header {header} does not match the channels {names}")
    group.time = [float(row[0]) for row in rows]
    for index, name in enumerate(names, start=1):
        group.channels[name] = [float(row[index]) if row[index] != "" else None for row in rows]


def _format_seconds(value):
    minutes, seconds = divmod(value, 60)
    return f"{int(minutes)}:{seconds:06.3f}"


def main(paths):
    status = 0
    for path in paths:
        try:
            session = read(path)
        except (LapVizFormatError, zipfile.BadZipFile, KeyError, json.JSONDecodeError, struct.error) as error:
            print(f"{path}: INVALID: {error}")
            status = 1
            continue
        info = session.manifest["session"]
        best = session.best_lap()
        print(f"{path}: {info['start']}, {info.get('driver', '?')}, {info.get('circuit', {}).get('name', '?')}")
        for group in session.groups.values():
            print(f"  {group.name}: {len(group.time)} samples, channels: {', '.join(group.channels) or 'none'}")
        print(f"  {len(session.laps())} laps" + (f", best lap {best['number']}: {_format_seconds(best['duration'])}" if best else ""))
        if session.events():
            print(f"  {len(session.events())} events: {', '.join(sorted({e['type'] for e in session.events()}))}")
    return status


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]) if len(sys.argv) > 1 else print(__doc__) or 2)
