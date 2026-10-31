# `LapViz.Telemetry`

_[![NuGet Version](https://img.shields.io/nuget/v/LapViz.Telemetry.svg?style=flat&label=NuGet%3A%20LapViz.Telemetry)](https://www.nuget.org/packages/LapViz.Telemetry/)_  

A .NET library that provides the core building blocks for **motorsport telemetry and timing**.  
It powers all LapViz applications (Laptimer, Stopwatch, and our forthcoming web platforms) and includes a reference client for [OpenLiveTiming.com](https://openlivetiming.com), our **open live timing platform**.

Overview for developers, API and the other LapViz apps: **[lapviz.com/developers](https://lapviz.com/developers)**.

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
