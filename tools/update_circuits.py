#!/usr/bin/env python3
"""Updates src/LapViz.Telemetry/Resources/circuits.json from the public API of lapviz.com.

    python3 tools/update_circuits.py

Rules:
- every circuit of lapviz.com with usable coordinates, with its id, name, location, country and weather link;
- the timing lines of lapviz.com, unless they are inconsistent (a line longer than 150 m or outside the box):
  then the lines already in the file are kept, and the circuit is reported;
- "useDirection" stays true: lapviz.com stores false everywhere, and the built-in list always detected the
  crossings toward the apex;
- circuits already in the file keep their order, new ones are added at the end.
"""

import json
import math
import string
import urllib.request
from pathlib import Path

API = "https://lapviz.com/api/circuit"
FILE = Path(__file__).resolve().parent.parent / "src" / "LapViz.Telemetry" / "Resources" / "circuits.json"
MAX_LINE_METERS = 150
BOX_MARGIN = 0.002


def get(url):
    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)


def distance(a, b):
    p1, p2 = math.radians(a[0]), math.radians(b[0])
    dp, dl = p2 - p1, math.radians(b[1] - a[1])
    return 2 * 6371000 * math.asin(math.sqrt(math.sin(dp / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(dl / 2) ** 2))


def point(p):
    return [p["latitude"], p["longitude"]]


def fetch():
    """Configurations of all the circuits: listed by the search, read by region (the API limits the radius)."""
    circuits = {}
    for text in string.ascii_lowercase + string.digits:
        for circuit in get(f"{API}/search?text={text}&p=1&s=1000")["circuits"]:
            circuits[circuit["id"]] = circuit

    configurations, covered, failed = {}, set(), []
    for circuit in circuits.values():
        if circuit["id"] in covered:
            continue
        center = (circuit["latitude"], circuit["longitude"])
        try:
            radius = 200
            found = get(f"{API}/configurations?lastUpdateDateTime=2000-01-01T00:00:00Z&lat={center[0]}&lon={center[1]}&radius={radius}")
        except Exception:
            try:  # A broken circuit in the region fails the whole request: this one alone
                radius = 1
                found = get(f"{API}/configurations?lastUpdateDateTime=2000-01-01T00:00:00Z&lat={center[0]}&lon={center[1]}&radius={radius}")
            except Exception:
                found = []
                failed.append(circuit["id"])
        for configuration in found:
            configurations[configuration["id"]] = configuration
        covered.add(circuit["id"])
        if radius == 200:
            covered.update(x["id"] for x in circuits.values() if distance(center, (x["latitude"], x["longitude"])) < 190_000)

    return list(configurations.values()), failed


def problems(box, segments):
    lats, lons = [box[0][0], box[1][0]], [box[0][1], box[1][1]]
    found = []
    if max(lats) - min(lats) < 1e-5 or max(lons) - min(lons) < 1e-5:
        found.append("empty box")
    for number, start, end in segments:
        if distance(start, end) > MAX_LINE_METERS:
            found.append(f"line {number}: {distance(start, end):.0f} m")
        elif any(p[0] < min(lats) - BOX_MARGIN or p[0] > max(lats) + BOX_MARGIN or
                 p[1] < min(lons) - BOX_MARGIN or p[1] > max(lons) + BOX_MARGIN for p in (start, end)):
            found.append(f"line {number}: outside the box")
    return found


def main():
    current = json.loads(FILE.read_text(encoding="utf-8-sig"))
    by_code = {c["code"].lower(): c for c in current}
    configurations, failed = fetch()

    updated, report = {}, []
    for c in sorted(configurations, key=lambda x: x["id"]):
        code = c["code"].lower()
        box = [point(c["boundingBox"]["start"]), point(c["boundingBox"]["end"])]
        segments = [[s["number"], point(s["boundary"]["start"]), point(s["boundary"]["end"])] for s in sorted(c["segments"], key=lambda s: s["number"])]
        old = by_code.get(code)
        issues = problems(box, segments)
        if issues and old:
            report.append(f"{code}: lapviz.com {', '.join(issues)}, lines of the file kept")
            box, segments = old["box"], old["segments"]
        elif issues:
            report.append(f"{code}: lapviz.com {', '.join(issues)}, added anyway")
        elif old and old["segments"] != segments:
            report.append(f"{code}: timing lines updated")

        entry = {"id": c["id"], "name": c["name"], "location": c.get("location"), "code": c["code"],
                 "country": c.get("countryCode"), "useDirection": True, "zoom": c["zoom"],
                 "weather": c.get("weatherForecastCode"), "test": c.get("test") or None, "updated": c.get("updated"),
                 "center": point(c["center"]), "box": box, "segments": segments}
        if c.get("type", 1) != 1:
            entry["type"] = "Open"
        updated[code] = {k: v for k, v in entry.items() if v not in (None, "")}

    missing = [code for code in by_code if code not in updated]
    for code in missing:  # Not on lapviz.com any more (or failed): kept as is
        updated[code] = by_code[code]
        report.append(f"{code}: not found on lapviz.com, kept")

    order = list(by_code) + sorted(code for code in updated if code not in by_code)
    lines = [json.dumps(updated[code], ensure_ascii=False, separators=(",", ":")) for code in order]
    FILE.write_bytes(("[\r\n" + ",\r\n".join(lines) + "\r\n]\r\n").encode("utf-8"))

    print(f"{len(lines)} circuits ({len(lines) - len(by_code)} new), API failures: {failed or 'none'}")
    print("\n".join(report))


if __name__ == "__main__":
    main()
