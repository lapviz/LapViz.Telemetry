# LapViz telemetry format, version 2

The LapViz format (`.lvz`) stores a motorsport session recorded by a data logger, a lap timer or a phone:
the samples of its channels (GPS position, speed, RPM, temperatures, accelerations...), the laps and sectors
measured by one or several timing sources, and a description of the session (driver, vehicle, device, circuit,
setup of the vehicle, notes).

It is an **open format**: anyone may read and write it, without fee or permission. The reference implementation
is the open source [LapViz.Telemetry](https://github.com/lapviz/lapviz.telemetry) library (MIT license):
`LapVizFile` (read and write), `LapVizDataReader` and `LapVizDataWriter`.

Design goals:

* **Simple to implement** in any language: a ZIP archive, a JSON manifest, little endian numbers or CSV.
* **Lossless and complete**: full resolution samples, units, several timing sources, free metadata.
* **Efficient for analysis**: samples in columns, read in one pass, compact once compressed.
* **Streamable**: a logger can append CSV rows while it records.
* **Extensible**: unknown members and archive entries are ignored by readers.

The key words "MUST", "SHOULD" and "MAY" are to be interpreted as described in RFC 2119.

To implement the format:

| | |
|---|---|
| [`lapviz-format.schema.json`](lapviz-format.schema.json) | JSON Schema (2020-12) of the manifest: validate the `lapviz.json` you write. |
| [`examples/`](examples) | Example files: `binary.lvz` (byte-shuffled binary samples), `csv.lvz` (the same samples in CSV), `laptimes-only.lvz` (laps without samples), each with its manifest next to it. They use every member of this document. |
| [`tools/python/lvz.py`](../tools/python/lvz.py) | A reader in plain Python (no dependency, about 150 lines), written from this document only: a second implementation to compare yours with. |

The CI checks that they agree: the examples are read by the .NET library and by the Python reader, the binary and
CSV examples hold the same samples, and every manifest, the one below included, is valid against the schema.

## 1. Archive

A LapViz file is a ZIP archive (PKWARE APPNOTE), with the `.lvz` extension. Its entries are either *stored* or
compressed with *deflate*: readers MUST support both, writers MUST NOT use other compression methods.
The media type is `application/vnd.lapviz.telemetry+zip`.

| Entry | Required | Content |
|---|---|---|
| `lapviz.json` | yes | The manifest (section 2). |
| `samples.bin` or `samples.csv` | no | The samples, binary (section 3) or CSV (section 4), as declared in the manifest. A session without samples (lap times only) has none. |
| any other entry | no | Extensions (video, setup sheet...). Names SHOULD be prefixed by the name of the producer (`acme/setup.json`). Readers MUST ignore the entries they do not know. |

## 2. Manifest

`lapviz.json` is a UTF-8 JSON object (RFC 8259). Member names are case sensitive. Readers MUST ignore unknown
members; writers SHOULD omit the members that have no value rather than write `null`.

```json
{
  "format": "lapviz",
  "version": 2,
  "generator": { "name": "LapViz Laptimer", "version": "26.10.3" },
  "session": {
    "start": "2026-09-12T10:00:00.000+02:00",
    "clock": "gps",
    "driver": "Alice Racer",
    "vehicle": "OTK Tony Kart",
    "device": { "brand": "AiM", "model": "Mychron 5", "serial": "51234", "firmware": "1.2" },
    "circuit": { "code": "genk", "name": "Karting Genk", "countryCode": "BE" },
    "description": "Free practice, new tyres",
    "sourceFile": "Alice_Genk_a_0708.xrk",
    "setup": {
      "tyres": "MG Red", "tyreCondition": "new", "tyreLaps": 0,
      "pressureFront": 0.65, "pressureRear": 0.70, "gearingFront": 11, "gearingRear": 80,
      "chassis": "Medium axle, rear width 1390 mm", "feeling": 4
    },
    "notes": "Oversteer out of turn 3",
    "properties": { "weather": "dry" }
  },
  "channels": [
    { "name": "Latitude", "unit": "deg", "type": "float64", "rate": 10, "origin": "gps" },
    { "name": "Longitude", "unit": "deg", "type": "float64", "rate": 10, "origin": "gps" },
    { "name": "GPS Speed", "unit": "km/h", "type": "float32", "rate": 10, "origin": "gps" },
    { "name": "RPM", "unit": "rpm", "type": "float32", "rate": 20, "origin": "ecu" },
    { "name": "Gear", "type": "float32", "origin": "computed", "interpolation": "step" }
  ],
  "samples": {
    "file": "samples.bin", "encoding": "binary", "count": 5888, "byteShuffle": true,
    "sha256": "4f0c6b1e9a2d7c3b8e5f1a6d9c2b7e4a1f8d3c6b9e2a5d8c1f4b7e0a3d6c9b2e"
  },
  "timing": [
    {
      "source": "AiM",
      "device": true,
      "circuit": {
        "code": "genk", "name": "Karting Genk", "type": "closed",
        "lines": [
          [ { "lat": 50.9820120, "lon": 5.3301240 }, { "lat": 50.9821530, "lon": 5.3303110 } ],
          [ { "lat": 50.9832050, "lon": 5.3311280 }, { "lat": 50.9833410, "lon": 5.3309020 } ],
          [ { "lat": 50.9826770, "lon": 5.3285430 }, { "lat": 50.9825010, "lon": 5.3287150 } ]
        ]
      },
      "laps": [
        { "number": 0, "start": 12.100, "duration": 21.428, "flags": [ "pitOut" ] },
        { "number": 1, "start": 33.528, "duration": 73.863, "sectors": [ 24.511, 25.903, 23.449 ] },
        { "number": 2, "start": 107.391, "duration": 79.102, "flags": [ "neutralized" ] }
      ]
    },
    {
      "source": "Results sheet",
      "declared": true,
      "laps": [ { "number": 1, "start": 33.5, "duration": 73.86 } ]
    }
  ],
  "events": [
    { "time": 61.250, "type": "marker", "label": "Kerb in turn 4", "source": "driver" },
    { "time": 109.020, "type": "flag", "label": "yellow" }
  ]
}
```

| Member | Type | Required | Description |
|---|---|---|---|
| `format` | string | yes | Always `"lapviz"`. |
| `version` | integer | yes | Major version of this specification: `2`. A reader MUST refuse a version it does not support. Compatible additions do not change the version. |
| `generator` | object | no | Software that wrote the file: `name`, `version` (strings). |
| `session` | object | yes | Description of the session (2.1). |
| `channels` | array | yes | The channels of the samples, in the order of the columns (2.2). May be empty. |
| `samples` | object | no | Where and how the samples are stored (2.3). Absent: no samples. |
| `timing` | array | no | Timing sources and their laps (2.4). |
| `events` | array | no | Instants of the session: markers, beacons, flags (2.5). |

### 2.1 Session

| Member | Type | Required | Description |
|---|---|---|---|
| `start` | string | yes | Instant of the time origin of the samples and of the laps: ISO 8601 date and time with an offset (`Z` for UTC), at least millisecond precision recommended. |
| `clock` | string | no | Clock `start` was taken from: `"gps"` (time of the satellites, exact to the millisecond), `"network"` (a clock synchronized over a network, e.g. a phone), `"device"` (the clock of the logger, set by hand or never: it may be off by minutes, or by years) or `"manual"` (typed by a person). Absent: unknown. A reader that compares sessions of different files (a lap timer and a transponder, a logger and a video) SHOULD trust `"gps"` and `"network"` only. |
| `driver` | string | no | Name of the driver. |
| `vehicle` | string | no | Vehicle (make, model). |
| `device` | object | no | Logger: `brand`, `model`, `serial`, `firmware` (strings). |
| `circuit` | object | no | Circuit (2.1.2). |
| `description` | string | no | Free text. |
| `sourceFile` | string | no | Name of the file the session was converted from. |
| `setup` | object | no | Setup of the vehicle and feeling of the driver (2.1.1). |
| `notes` | string | no | Free notes of the driver or the team: conditions, changes, feeling. Line ends allowed. |
| `properties` | object | no | Free metadata: string values only. |

#### 2.1.1 Setup

The setup sheet of the session, as known when the file was written (karting first, every member optional). Writers
SHOULD omit `setup` when it has no member. Readers MUST ignore a member of the wrong type or out of its range, and
the members they do not know.

| Member | Type | Description |
|---|---|---|
| `tyres` | string | Tyres: make, compound (`"MG Red"`). |
| `tyreCondition` | string | State of the tyres at the start of the session: `"new"`, `"scrubbed"` (heated once for a few laps), `"used"` or `"worn"`. Readers SHOULD compare it case insensitively. |
| `tyreLaps` | integer | Laps already driven on the set of tyres at the start of the session, `0` or more. |
| `pressureFront`, `pressureRear` | number | Cold pressures of the front and rear tyres, in bar. |
| `gearingFront`, `gearingRear` | integer | Sprockets, number of teeth: front (pinion) and rear (crown). |
| `carburation` | string | Carburation settings (jets, needle...). |
| `chassis` | string | Chassis settings: axle, track width, ride height, stiffness... |
| `feeling` | integer | Feeling of the driver with this setup, from `1` (bad) to `5` (perfect). |

A setup that changes after the session (edited in an application) does not require to rewrite the file: the
file keeps the setup known when it was written, an application MAY keep the current one elsewhere and write it
when it exports the session.

#### 2.1.2 Circuit

The circuit of the session, or the configuration a timing source measured its laps with (2.4). Every member is optional.

| Member | Type | Description |
|---|---|---|
| `code` | string | Short identifier, e.g. the LapViz circuit code. |
| `name` | string | Name of the circuit. |
| `countryCode` | string | ISO 3166-1 alpha-2. |
| `type` | string | `"closed"`: a loop, the first line starts and ends each lap. `"open"`: point to point (hill climb, rally stage), the last line ends the lap. Absent: unknown, readers SHOULD assume `"closed"`. |
| `lines` | array | The timing lines, in order: the start line (also the finish line of a closed circuit), then the sector lines, then the finish line of an open circuit. |

A line is an array of two points, its ends, across the track. A point is an object `{ "lat": 50.98201, "lon": 5.33012 }`:
WGS 84 decimal degrees, south and west negative. The members are named on purpose: formats differ on the order of
latitude and longitude (GeoJSON puts the longitude first), and a position read in the wrong order is still a valid
position. Readers MUST ignore a line that is not two points of a latitude in [-90, 90] and a longitude in [-180, 180].

With its lines, a file is self-contained: another software can check the laps, or measure them again from the
positions, without knowing the circuit. Writers SHOULD write the lines of the circuit a timing source measured with.

### 2.2 Channels

| Member | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | Name of the channel, unique in the file (case insensitive), not empty. |
| `unit` | string | no | Unit (see 5.2). |
| `type` | string | no | Binary encoding: `"float32"` or `"float64"` (default `"float64"`). Ignored by the CSV encoding. |
| `description` | string | no | Free text. |
| `rate` | number | no | Rate in Hz at which the logger measured the channel, strictly positive. It may differ from the rate of the samples: a 50 Hz RPM stored at the 10 Hz of the GPS, or a 1 Hz heart rate repeated at 25 Hz. |
| `origin` | string | no | What measured the channel: `"gps"`, `"imu"` (accelerometer, gyroscope of the logger), `"ecu"` (the engine or the vehicle, e.g. over CAN), `"obd"`, `"analog"` (a sensor wired to the logger), `"computed"` (by the logger or a software, from other channels). Absent: unknown. A wheel speed and a GPS speed are told apart by their origin, not by a guess on their name. |
| `interpolation` | string | no | `"linear"` (default): a continuous quantity, a reader MAY interpolate between two samples. `"step"`: a discrete quantity (gear, state, flag), a value holds until the next one; a reader MUST NOT interpolate it. |

The time is not a channel: it is the first column of the samples.

### 2.3 Samples

| Member | Type | Required | Description |
|---|---|---|---|
| `file` | string | yes | Name of the archive entry: `samples.bin` or `samples.csv`. |
| `encoding` | string | yes | `"binary"` (section 3) or `"csv"` (section 4). |
| `count` | integer | binary | Number of samples. Required for the binary encoding, informative for CSV. |
| `byteShuffle` | boolean | no | Binary encoding: the bytes of each column are shuffled (3.2). Default `false`. |
| `sha256` | string | no | SHA-256 of the content of the entry (uncompressed), 64 hexadecimal digits. Writers SHOULD write it. Readers MAY check it; a reader that checks it MUST refuse a file whose samples do not match. |

### 2.4 Timing sources

A timing source is something that measured laps: the logger itself (timing beacon, its own GPS detection),
a lap timer, a timekeeping system, an analysis software. A file MAY hold several sources for the same
session; readers SHOULD prefer the first source with `device` true, else the first source that is not `declared`.

| Member | Type | Required | Description |
|---|---|---|---|
| `source` | string | yes | Name of the source (`"AiM"`, `"LapViz"`, `"Transponder"`...). |
| `device` | boolean | no | True when the logger of the session measured these laps. Default `false`. |
| `declared` | boolean | no | True when the laps were not measured but typed in by a person or read from a document (a results sheet, a screenshot). Default `false`. |
| `circuit` | object | no | Circuit configuration used by the source (2.1.2), with its lines when known. |
| `laps` | array | yes | The laps, ordered by start. |

A lap:

| Member | Type | Required | Description |
|---|---|---|---|
| `number` | integer | yes | Lap number. `0` is the out lap (from the pit lane to the first crossing of the line). |
| `start` | number | yes | Seconds from `session.start` to the crossing that started the lap. |
| `duration` | number | complete laps | Lap time in seconds, strictly positive. Absent: the lap is incomplete (the session ended before the line); its `sectors` are the sectors completed so far. |
| `sectors` | array of numbers | no | Sector times in seconds, in order, from the first sector. The sum of the sectors of a complete lap is the lap time. |
| `flags` | array of strings | no | Status of the lap (below). Absent: a normal lap. |
| `driver` | string | no | Driver of the lap when it is not `session.driver`: a change of driver during an endurance race. |

The flags of a lap, case sensitive; readers MUST ignore the flags they do not know:

| Flag | Meaning |
|---|---|
| `pitOut` | The lap started in the pit lane (an out lap, which is not always lap `0`). |
| `pitIn` | The lap ended in the pit lane. |
| `invalid` | The lap does not count: track limits, penalty, cut. Readers MUST NOT take it as a best lap. |
| `neutralized` | The lap was driven under a yellow flag, a safety car or a red flag. Readers SHOULD NOT take it as a best lap. |

Times are decimal numbers of seconds; writers SHOULD keep at least the millisecond. A crossing of the line that does
not end a lap (e.g. the first crossing of a session, often logged as a lap of zero duration) is not a lap: it is the
`start` of the following one.

### 2.5 Events

Things that happened at an instant of the session and are not laps: a marker the driver set with a button of the
logger, the crossing of a timing beacon as the logger recorded it, a flag shown by the marshals, the entry in the pit
lane. Writers SHOULD order them by time.

| Member | Type | Required | Description |
|---|---|---|---|
| `time` | number | yes | Seconds from `session.start`. |
| `type` | string | yes | Type of the event (below). |
| `label` | string | no | Free text: the name of a marker, the colour of a flag. |
| `source` | string | no | What recorded the event: the name of a timing source, `"driver"`, a software. |

Types: `"marker"` (set by the driver or a person), `"beacon"` (crossing of a timing beacon or line, raw, as logged),
`"pitIn"`, `"pitOut"`, `"flag"` (the label is the colour: `"yellow"`, `"red"`, `"green"`, `"chequered"`...).
Readers MUST ignore an event without `time` or `type`, and SHOULD keep the events of a type they do not know.

## 3. Binary samples

### 3.1 Layout

`samples.bin` holds `count` samples in columns, one after the other, without header nor padding, all little endian:

1. the time: `count` signed 64-bit integers, microseconds since `session.start`, strictly increasing;
2. then, for each channel in the order of `channels`, `count` IEEE 754 numbers of its `type`
   (4 bytes for `float32`, 8 bytes for `float64`).

A missing value is a NaN (any NaN). The length of the entry is therefore exactly
`count × (8 + Σ size of the channels)` bytes.

### 3.2 Byte shuffle

When `byteShuffle` is true, the bytes of each column (the time included) are stored transposed: for a column of
`n` values of `s` bytes, the byte `j` (`0 ≤ j < s`, little endian order) of the value `i` is at offset `j × n + i`
of the column. Neighbouring samples share their high-order bytes: grouped together, they compress much better
with deflate. The layout of 3.1 (column after column) is unchanged.

Writers SHOULD shuffle and deflate the binary samples. They SHOULD use `float64` for the positions
(a `float32` latitude is only precise to about a meter) and MAY use `float32` for the other channels.

## 4. CSV samples

`samples.csv` is UTF-8 text, RFC 4180: comma separator, CRLF or LF line ends, fields with a comma, a quote or a
line end between double quotes.

* The first line is the header: `time`, then the name of each channel, in the order of `channels`.
* Each following line is a sample: the time in seconds since `session.start` (decimal point, up to 6 decimals),
  then the value of each channel, decimal point, no thousands separator, scientific notation allowed.
* An empty field is a missing value.
* Times are strictly increasing.

A logger can append lines while it records and write `lapviz.json` (with the laps) when the session ends.

## 5. Conventions

### 5.1 Channel names

Readers and writers SHOULD map the channels they know to these names, so that the files of different
loggers can be compared:

| Name | Unit | Description |
|---|---|---|
| `Latitude`, `Longitude` | deg | WGS 84, decimal degrees (south and west negative). |
| `Altitude` | m | Above sea level. |
| `Distance` | m | Distance travelled since the start of the session. |
| `GPS Speed` | km/h | Speed measured by the GPS. |
| `Speed` | km/h | Wheel or vehicle speed. |
| `Heading` | deg | Course over ground, 0 = north, clockwise. |
| `GPS Accuracy` | m | Horizontal accuracy of the position. |
| `RPM` | rpm | Engine speed. |
| `GPS LatAcc`, `GPS LonAcc` | g | Lateral and longitudinal accelerations computed from the GPS. |
| `AccelerometerX`, `AccelerometerY`, `AccelerometerZ` | g | Accelerometer of the logger. |
| `GyroX`, `GyroY`, `GyroZ` | deg/s | Gyroscope of the logger. |
| `Water Temp`, `Exhaust Temp` | °C | Temperatures. |
| `Throttle`, `Brake` | % | Pedal positions. |
| `Steering Angle` | deg | Positive to the right. |
| `Gear` | | Engaged gear (`interpolation` `"step"`). |
| `Satellites` | | Number of satellites used by the GPS fix. |
| `HDOP` | | Horizontal dilution of precision of the GPS. |
| `Wheel Speed FL`, `Wheel Speed FR`, `Wheel Speed RL`, `Wheel Speed RR` | km/h | Speed of each wheel (front left, front right, rear left, rear right). |
| `Oil Temp`, `Oil Pressure` | °C, bar | Engine oil. |
| `CHT` | °C | Cylinder head temperature (air-cooled engines). |
| `Lambda` | | Air-fuel equivalence ratio (1 = stoichiometric). |
| `Battery` | V | Voltage of the battery. |
| `Tyre Temp FL`, `Tyre Temp FR`, `Tyre Temp RL`, `Tyre Temp RR` | °C | Surface temperature of each tyre. |
| `Air Temp`, `Track Temp` | °C | Weather. |
| `Heart Rate` | bpm | Heart rate of the driver. |

### 5.2 Units

Units are free text; writers SHOULD use: `s`, `m`, `km/h`, `m/s`, `deg`, `deg/s`, `g`, `m/s2`, `rpm`, `°C`, `%`,
`bar`, `hPa`, `V`, `A`, `l`, `bpm`.

A channel with a name of 5.1 SHOULD be in the unit of the table: a writer converts the values (mph, ft, psi, °F...)
rather than keep the unit of the logger, so that the files of different loggers compare without conversion. A reader
that finds a channel of 5.1 in another unit SHOULD convert it, or treat it as an unknown channel.

## 6. Version 1 (legacy)

Version 1 files have the same `.lvz` extension. A reader recognizes the version:

* a ZIP archive with a `lapviz.json` entry: version 2;
* a ZIP archive without it: version 1, the first entry is the text below;
* a text file whose first line is `#Format=LapViz Delimited Data`: version 1.

Version 1 is a line oriented text:

```
#Format=LapViz Delimited Data
#Version=1
#CircuitCode=genk
#Fields=Latitude,Longitude,Speed,Accuracy
#Event=1726135233528,Lap,1,0,738630000
1726135200000,50.9821,5.3302,72.5,3
```

* `#Fields=`: the channels; each data line is the Unix time in milliseconds, then the values (empty: missing).
* `#Event=`: Unix time in milliseconds of the crossing, type (`Lap` or `Sector`), lap number, sector number,
  lap or sector time in .NET ticks (100 ns).

Readers SHOULD keep reading version 1; writers SHOULD write version 2.

## 7. Changes

Compatible additions do not change the version: a reader written before them ignores the new members, as it ignores
every unknown member, and reads the rest of the file as before.

* **2026-10-30**: `session.clock`; the `type` and `lines` of a circuit (2.1.2); the `rate`, `origin` and `interpolation`
  of a channel; `samples.sha256`; `declared` timing sources; the `flags` and `driver` of a lap; `events` (2.5); more
  channel names (5.1); units of the channel names (5.2).
* **2026-10-17**: `session.setup` and `session.notes`.
* **2026-10-03**: version 2: ZIP archive, JSON manifest, binary or CSV samples.
