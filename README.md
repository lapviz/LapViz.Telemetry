# `LapViz.Telemetry`

_[![NuGet Version](https://img.shields.io/nuget/v/LapViz.Telemetry.svg?style=flat&label=NuGet%3A%20LapViz.Telemetry)](https://www.nuget.org/packages/LapViz.Telemetry/)_  

A .NET library that provides the core building blocks for **motorsport telemetry and timing**.  
It powers the LapViz applications (the lap timer app, Stopwatch and [lapviz.com](https://lapviz.com)) and includes a reference client for [OpenLiveTiming.com](https://openlivetiming.com), our **open live timing platform**.

Overview for developers, API and the other LapViz apps: **[lapviz.com/developers](https://lapviz.com/developers)**.

Open your `.xrk`, `.vbo`, `.rcz` files in the browser at **[lapviz.com](https://lapviz.com)**: no install, laps, channels and a 3D replay
([all supported formats](https://lapviz.com/formats)).

---

## Table of Contents

1. [Features](#features)  
2. [Installing](#installing)  
3. [Documentation](#documentation)  
4. [Examples](#examples)  
5. [Community](#community)  
6. [Code of Conduct](#code-of-conduct)  
7. [License](#license)

---

## Features

* 🏎 **Core telemetry domain model**  
  Sessions, laps, drivers, devices, telemetry events, and channels.

* ⏱ **Timing utilities**  
  Work with laps, sectors, positions, timestamps, and live updates.

* 🏁 **Timing from passings**  
  `PassingTimingSession` times a session from the passings of transponders (decoder loops), LapViz devices
  or line crossings: laps and sectors, minimum and maximum lap time, duplicates and missed loops, start signal or first crossing,
  checkered flag by laps, time or hand, red flag (suspension and restart, clock stopped), minimum pit stop time,
  penalties, classification by best lap or as a race, and the race model of
  OpenLiveTiming (`RaceSessionState`). Passings and commands form a journal: the result is the same whatever their
  order of arrival.
  `TimingJournal` writes and replays them (NDJSON), `PassingSimulator` generates the passings of a field (missed and
  duplicated passings, pit stops) and `LapViz.Telemetry.Timing.P3` reads and writes the MYLAPS P3 decoder protocol
  (framing, escaping, CRC, passing and status records; not yet checked against a real decoder).

* 📦 **Open file format**  
  The LapViz format (`.lvz`) stores a session: every channel at its own rate and full resolution, with its units, laps and sectors of
  several timing sources, driver, vehicle, device, circuit, setup sheet and notes. It is open and documented in
  [docs/lapviz-format.md](docs/lapviz-format.md), with a [JSON Schema](docs/lapviz-format.schema.json) of its manifest,
  [example files](docs/examples) and a [reader in plain Python](tools/python/lvz.py); `LapVizFile` reads and writes it,
  and the readers of the other formats convert to it (`LapVizFile.FromDeviceSessionData`).

* 🗺 **Built-in circuits**  
  More than 200 circuits with their start/finish and sector lines, used to time GPS sessions. They come from
  [lapviz.com](https://lapviz.com/circuits) and are updated every week. Missing a circuit, or found a wrong line?
  [Propose it on lapviz.com](https://lapviz.com/circuitsubmit), no account needed (see [CONTRIBUTING.md](CONTRIBUTING.md#circuits)).

* 🌐 **Open live timing client**  
  Built-in SignalR client for [OpenLiveTiming.com](https://openlivetiming.com), serving as both a **ready-to-use client** and **reference integration** for laptimer apps or timekeeping systems. We also plan to release our own iOS & Android Laptimer (primarily used for tests) as open source, to encourage integration with existing laptimers, whether hardware-based or app-based.

---

## Installing

The fastest way to get started is by installing the NuGet package:

```bash
dotnet add package LapViz.Telemetry
```

---

## Documentation

Detailed documentation and API references are available on the project website:  
👉 [https://lapviz.com/swagger/index.html](https://lapviz.com/swagger/index.html)

---

## Examples

The snippets below target the current API (`LapViz.Telemetry.IO`). The [example files](docs/examples) of the format
are a good input to try them.

### Read a session and print its laps

```csharp
using System;
using System.Linq;
using LapViz.Telemetry.IO;

var file = LapVizFile.Read("session.lvz");
Console.WriteLine($"{file.Session.Driver} at {file.Session.Circuit?.Name}, {file.Session.Start:yyyy-MM-dd HH:mm}");

// The laps of the logger when it timed them, else those of another timing source
var timing = file.PreferredTiming;
if (timing != null)
{
    foreach (var lap in timing.Laps.Where(x => x.IsComplete))
        Console.WriteLine($"Lap {lap.Number}: {TimeSpan.FromSeconds(lap.Duration!.Value):m\\:ss\\.fff}");

    // Fastest lap, leaving out invalid laps and those that start or end in the pits
    var best = timing.Laps
        .Where(x => x.IsValid && !x.HasFlag(LapVizLapFlags.PitOut) && !x.HasFlag(LapVizLapFlags.PitIn))
        .OrderBy(x => x.Duration)
        .FirstOrDefault();
    Console.WriteLine($"Best: {best}");
}

// Channels, by group of samples (each group has its own rate)
foreach (var group in file.Groups)
    Console.WriteLine($"{group.Name}: {string.Join(", ", group.Channels.Select(x => $"{x.Name} ({x.Unit})"))}");
```

With [docs/examples/binary.lvz](docs/examples/binary.lvz) it prints:

```text
Alex Example at Example oval (fictitious), 2026-09-12 10:00
Lap 0: 0:18.000
Lap 1: 1:01.240
Lap 2: 1:00.115
Best: Lap 2: 60.115 s
main: Latitude (deg), Longitude (deg), GPS Speed (km/h), RPM (rpm), Gear ()
```

### Write a session

```csharp
using System;
using System.IO;
using LapViz.Telemetry.IO;

var session = new LapVizFile(DateTimeOffset.Now);
session.Session.Driver = "Alex";

// Time of the samples in microseconds since the start, then one value per sample and channel
session.SetTime(new long[] { 0, 100_000, 200_000 });
session.AddChannel(new LapVizChannel("Speed", "km/h"), new[] { 92.4, 93.1, 93.8 });

// Lap start and duration in seconds since the start of the session
var laps = new LapVizTimingSource { Source = "My lap timer", IsDevice = true };
laps.Laps.Add(new LapVizLap { Number = 1, Start = 0, Duration = 61.240 });
laps.Laps.Add(new LapVizLap { Number = 2, Start = 61.240, Duration = 60.115 });
session.Timing.Add(laps);

using (var stream = File.Create("session.lvz"))
    session.Write(stream);
```

The format itself is described in [docs/lapviz-format.md](docs/lapviz-format.md). For a complete application (a lap timer
and a live timing client on the command line), see [samples/LapViz.Telemetry.CLI](samples/LapViz.Telemetry.CLI).

---

## Community

LapViz.Telemetry is more than just a library — it’s the foundation for building an **open ecosystem** around live motorsport timing.  

We encourage developers, track owners, and timekeepers worldwide to integrate with [OpenLiveTiming.com](https://openlivetiming.com) and help shape the future of accessible timing solutions.

- 🌐 [LapViz for developers](https://lapviz.com/developers) and [the LapViz apps](https://lapviz.com/apps)
- 💬 [Join our Discord Community](https://discord.gg/S6pnw2Dpa2)
- 🤝 Contribute by reading our [Contributing Guidelines](CONTRIBUTING.md)

---

## Code of Conduct

This project has adopted the [Contributor Covenant](CODE_OF_CONDUCT.md) to clarify expected behavior in our community.

---

## License

Copyright © Mengal SRL

`LapViz.Telemetry` is provided as-is under the MIT license.  
For more information, see [LICENSE](LICENSE).
