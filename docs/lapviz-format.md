# LapViz telemetry format, version 2

The LapViz format (`.lvz`) stores a motorsport session recorded by a data logger, a lap timer or a phone:
the samples of its channels (GPS position, speed, RPM, temperatures, accelerations...), the laps and sectors
measured by one or several timing sources, and a description of the session (driver, vehicle, device, circuit).

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
    "driver": "Alice Racer",
    "vehicle": "OTK Tony Kart",
    "device": { "brand": "AiM", "model": "Mychron 5", "serial": "51234", "firmware": "1.2" },
    "circuit": { "code": "genk", "name": "Karting Genk", "countryCode": "BE" },
    "description": "Free practice, new tyres",
    "sourceFile": "Alice_Genk_a_0708.xrk",
    "properties": { "weather": "dry", "tyres": "MG Red" }
  },
  "channels": [
    { "name": "Latitude", "unit": "deg", "type": "float64" },
    { "name": "Longitude", "unit": "deg", "type": "float64" },
    { "name": "GPS Speed", "unit": "km/h", "type": "float32" },
    { "name": "RPM", "unit": "rpm", "type": "float32" }
  ],
  "samples": { "file": "samples.bin", "encoding": "binary", "count": 5888, "byteShuffle": true },
  "timing": [
    {
      "source": "AiM",
      "device": true,
      "circuit": { "code": "genk", "name": "Karting Genk" },
      "laps": [
        { "number": 0, "start": 12.100, "duration": 21.428 },
        { "number": 1, "start": 33.528, "duration": 73.863, "sectors": [ 24.511, 25.903, 23.449 ] }
      ]
    }
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

### 2.1 Session

| Member | Type | Required | Description |
|---|---|---|---|
| `start` | string | yes | Instant of the time origin of the samples and of the laps: ISO 8601 date and time with an offset (`Z` for UTC), at least millisecond precision recommended. |
| `driver` | string | no | Name of the driver. |
| `vehicle` | string | no | Vehicle (make, model). |
| `device` | object | no | Logger: `brand`, `model`, `serial`, `firmware` (strings). |
| `circuit` | object | no | Circuit: `code` (short identifier, e.g. the LapViz circuit code), `name`, `countryCode` (ISO 3166-1 alpha-2). |
| `description` | string | no | Free text. |
| `sourceFile` | string | no | Name of the file the session was converted from. |
| `properties` | object | no | Free metadata: string values only. |

### 2.2 Channels

| Member | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | Name of the channel, unique in the file (case insensitive), not empty. |
| `unit` | string | no | Unit (see 5.2). |
| `type` | string | no | Binary encoding: `"float32"` or `"float64"` (default `"float64"`). Ignored by the CSV encoding. |
| `description` | string | no | Free text. |

The time is not a channel: it is the first column of the samples.

### 2.3 Samples

| Member | Type | Required | Description |
|---|---|---|---|
| `file` | string | yes | Name of the archive entry: `samples.bin` or `samples.csv`. |
| `encoding` | string | yes | `"binary"` (section 3) or `"csv"` (section 4). |
| `count` | integer | binary | Number of samples. Required for the binary encoding, informative for CSV. |
| `byteShuffle` | boolean | no | Binary encoding: the bytes of each column are shuffled (3.2). Default `false`. |

### 2.4 Timing sources

A timing source is something that measured laps: the logger itself (timing beacon, its own GPS detection),
a lap timer, a timekeeping system, an analysis software. A file MAY hold several sources for the same
session; readers SHOULD prefer the first source with `device` true.

| Member | Type | Required | Description |
|---|---|---|---|
| `source` | string | yes | Name of the source (`"AiM"`, `"LapViz"`, `"Transponder"`...). |
| `device` | boolean | no | True when the logger of the session measured these laps. Default `false`. |
| `circuit` | object | no | Circuit configuration used by the source (same members as 2.1). |
| `laps` | array | yes | The laps, ordered by start. |

A lap:

| Member | Type | Required | Description |
|---|---|---|---|
| `number` | integer | yes | Lap number. `0` is the out lap (from the pit lane to the first crossing of the line). |
| `start` | number | yes | Seconds from `session.start` to the crossing that started the lap. |
| `duration` | number | complete laps | Lap time in seconds, strictly positive. Absent: the lap is incomplete (the session ended before the line); its `sectors` are the sectors completed so far. |
| `sectors` | array of numbers | no | Sector times in seconds, in order, from the first sector. The sum of the sectors of a complete lap is the lap time. |

Times are decimal numbers of seconds; writers SHOULD keep at least the millisecond. A crossing of the line that does
not end a lap (e.g. the first crossing of a session, often logged as a lap of zero duration) is not a lap: it is the
`start` of the following one.

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
| `Gear` | | Engaged gear. |

### 5.2 Units

Units are free text; writers SHOULD use: `s`, `m`, `km/h`, `m/s`, `deg`, `deg/s`, `g`, `m/s2`, `rpm`, `°C`, `%`,
`bar`, `hPa`, `V`, `A`, `l`.

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
