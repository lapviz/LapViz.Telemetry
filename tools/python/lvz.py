#!/usr/bin/env python3
# Copyright (c) 2025-2026 Mengal SRL. MIT license.
"""
Reference reader of the LapViz telemetry format, version 2 (docs/lapviz-format.md), in plain Python: no
dependency, so that the format can be checked against something else than the .NET library.

    from lvz import read
    session = read("session.lvz")
    session.channels["GPS Speed"]   # list of floats (None: missing value)
    session.time                    # seconds since session.start
    session.laps()                  # laps of the preferred timing source

Command line: python3 lvz.py file.lvz [...] prints a summary of each file and exits with 1 if one is invalid.
"""

import csv
import io
import json
import math
import struct
import sys
import zipfile
from dataclasses import dataclass, field

SUPPORTED_VERSION = 2
_SIZES = {"float32": (4, "f"), "float64": (8, "d")}


class LapVizFormatError(ValueError):
    """The file is not a valid LapViz file of a supported version."""


@dataclass
class LapVizSession:
    manifest: dict
    time: list = field(default_factory=list)  # Seconds since session.start, strictly increasing
    channels: dict = field(default_factory=dict)  # Name -> list of float or None

    @property
    def start(self) -> str:
        return self.manifest["session"]["start"]

    def timing(self):
        """The preferred timing source: the first one measured by the logger, else the first one (spec 2.4)."""
        sources = self.manifest.get("timing", [])
        return next((s for s in sources if s.get("device")), sources[0] if sources else None)

    def laps(self):
        source = self.timing()
        return source["laps"] if source else []

    def best_lap(self):
        complete = [lap for lap in self.laps() if lap.get("duration") and lap["number"] > 0]
        return min(complete, key=lambda lap: lap["duration"], default=None)


def read(path_or_stream) -> LapVizSession:
    with zipfile.ZipFile(path_or_stream) as archive:
        names = set(archive.namelist())
        if "lapviz.json" not in names:
            raise LapVizFormatError("Not a LapViz version 2 file: no lapviz.json entry")
        manifest = json.loads(archive.read("lapviz.json").decode("utf-8"))
        if manifest.get("format") != "lapviz":
            raise LapVizFormatError("format is not 'lapviz'")
        if manifest.get("version") != SUPPORTED_VERSION:
            raise LapVizFormatError(f"Unsupported version {manifest.get('version')!r}")
        if "start" not in manifest.get("session", {}):
            raise LapVizFormatError("session.start is missing")

        channels = manifest.get("channels", [])
        session = LapVizSession(manifest)
        samples = manifest.get("samples")
        if samples:
            if samples["file"] not in names:
                raise LapVizFormatError(f"Entry {samples['file']} is missing")
            data = archive.read(samples["file"])
            if samples["encoding"] == "binary":
                _read_binary(session, channels, samples, data)
            elif samples["encoding"] == "csv":
                _read_csv(session, channels, data)
            else:
                raise LapVizFormatError(f"Unknown encoding {samples['encoding']!r}")

        if any(b <= a for a, b in zip(session.time, session.time[1:])):
            raise LapVizFormatError("The times are not strictly increasing")
        return session


def _unshuffle(column: bytes, count: int, size: int) -> bytes:
    """Spec 3.2: byte j of value i is at offset j * count + i."""
    out = bytearray(len(column))
    for j in range(size):
        out[j::size] = column[j * count:(j + 1) * count]
    return bytes(out)


def _read_binary(session, channels, samples, data):
    count = samples["count"]
    shuffle = samples.get("byteShuffle", False)
    sizes = [_SIZES[c.get("type", "float64")] for c in channels]
    expected = count * (8 + sum(size for size, _ in sizes))
    if len(data) != expected:
        raise LapVizFormatError(f"samples.bin has {len(data)} bytes, {expected} expected")

    def column(offset, size):
        raw = data[offset:offset + count * size]
        return _unshuffle(raw, count, size) if shuffle else raw

    offset = 0
    session.time = [microseconds / 1e6 for microseconds in struct.unpack(f"<{count}q", column(0, 8))]
    offset += count * 8
    for channel, (size, code) in zip(channels, sizes):
        values = struct.unpack(f"<{count}{code}", column(offset, size))
        session.channels[channel["name"]] = [None if math.isnan(v) else v for v in values]
        offset += count * size


def _read_csv(session, channels, data):
    rows = list(csv.reader(io.StringIO(data.decode("utf-8-sig"))))
    header, rows = rows[0], [row for row in rows[1:] if row]
    names = [c["name"] for c in channels]
    if header != ["time"] + names:
        raise LapVizFormatError(f"CSV header {header} does not match the channels {names}")
    session.time = [float(row[0]) for row in rows]
    for index, name in enumerate(names, start=1):
        session.channels[name] = [float(row[index]) if row[index] != "" else None for row in rows]


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
        print(f"  {len(session.time)} samples, channels: {', '.join(session.channels) or 'none'}")
        print(f"  {len(session.laps())} laps" + (f", best lap {best['number']}: {_format_seconds(best['duration'])}" if best else ""))
    return status


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]) if len(sys.argv) > 1 else print(__doc__) or 2)
