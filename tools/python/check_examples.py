#!/usr/bin/env python3
# Copyright (c) 2025-2026 Mengal SRL. MIT license.
"""
Conformance of the specification kit, run by the CI: the example files are read by the reference reader of this folder
(independent of the .NET library), the binary and CSV examples hold the same samples, and every manifest (the examples
and the one in docs/lapviz-format.md) is valid against docs/lapviz-format.schema.json. Needs: pip install jsonschema.
"""

import json
import pathlib
import re
import sys

import jsonschema

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "python"))
from lvz import read  # noqa: E402


def main():
    schema = json.loads((ROOT / "docs" / "lapviz-format.schema.json").read_text(encoding="utf-8"))
    jsonschema.Draft202012Validator.check_schema(schema)
    validator = jsonschema.Draft202012Validator(schema, format_checker=jsonschema.FormatChecker())

    manifests = {path.name: json.loads(path.read_text(encoding="utf-8")) for path in sorted((ROOT / "docs" / "examples").glob("*.lapviz.json"))}
    spec = (ROOT / "docs" / "lapviz-format.md").read_text(encoding="utf-8")
    manifests["lapviz-format.md"] = json.loads(re.search(r"```json\n(.*?)```", spec, re.S).group(1))
    for name, manifest in manifests.items():
        validator.validate(manifest)
        print(f"{name}: valid manifest")

    binary, csv = read(ROOT / "docs" / "examples" / "binary.lvz"), read(ROOT / "docs" / "examples" / "csv.lvz")
    assert binary.time == csv.time, "binary and CSV times differ"
    types = {channel["name"]: channel.get("type", "float64") for channel in binary.manifest["channels"]}
    for name, values in binary.channels.items():
        tolerance = 1e-6 if types[name] == "float32" else 1e-12
        for x, y in zip(values, csv.channels[name]):
            assert (x is None) == (y is None) and (x is None or abs(x - y) <= tolerance * max(1, abs(y))), (name, x, y)
    print(f"binary.lvz and csv.lvz: the same {len(binary.time)} samples of {len(binary.channels)} channels")

    laptimes = read(ROOT / "docs" / "examples" / "laptimes-only.lvz")
    assert not laptimes.time and laptimes.best_lap()["duration"] == 60.115
    print("laptimes-only.lvz: laps without samples")


if __name__ == "__main__":
    main()
