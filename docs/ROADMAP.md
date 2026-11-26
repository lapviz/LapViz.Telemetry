# Roadmap

What is known to be open in the library, without a promised date. It is a list of ideas and known defects, not a
commitment: an item becomes work when someone takes it. To propose something else, or to take an item, open an issue
(see [CONTRIBUTING.md](../CONTRIBUTING.md)).

Each item carries a rough priority (**P1** next, **P2** useful, **P3** idea) and a size (**S** under a day, **M** a
few days, **L** a week or more, **XL** a project of its own). A done item is removed; an idea that is turned down is
marked "declined" with the reason, not deleted.

## Known defects

- [ ] **P1 · S** **LapViz.LiveTiming**: `SessionDataDto.LastUpdated` is a `DateTimeOffset` that cannot be null. A
  board carrying `null` (a session without any event) makes SignalR drop the message without an error. Servers can
  work around it by sending the creation date; the property should be nullable. In the same package,
  `ObservableLiveTimingClient` builds its views inside the SignalR handlers, where an exception is lost.
- [ ] **P1 · S** `TimingFormatters.FormatLapTime(119.9996 s)` prints `1:60.000` (the minutes are split before the
  rounding), and `ParseLapTime` loses a tick.
- [ ] **P1 · M** **First lap missed** by the detection of session events (`SessionEventDetection`) when sector 3 is
  not complete; noted on an earlier version, to reproduce first.
- [ ] **P3 · S** `DelimitedDataReader` dates a file that has no time column with the time of reading
  (`DateTimeOffset.Now`), so the same file gives two different sessions.

## Circuits

- [ ] **P1 · M** **Overlapping layouts**: the library takes the first circuit whose zone contains the position. The
  firmware of the LapViz timing devices keeps the candidates for 30 seconds and lets the start line decide; without
  the same rule, an app and a device can time two different layouts at the same place. The LapViz lap timer app
  already applies it in its own `CircuitSelector`: move that rule into the library.
- [ ] **P2 · M** **Several layouts per circuit** in `CircuitConfiguration`.
- [ ] **P3 · L** A line crossing detected from the kinematics (speed, acceleration) instead of the linear
  interpolation between two positions; to bring together with a GNSS and IMU fusion.

The data of `circuits.json` is not corrected here: it is synchronised from lapviz.com (see
[CONTRIBUTING.md](../CONTRIBUTING.md#circuits)).

## Model and API

Debt to treat before a major version (2.0), because each item breaks the public API.

- [ ] **P2 · L** **Asynchronous readers and writers**: `ITelemetryDataReader` and `ITelemetryDataWriter`, a
  receiver read as a stream like the other readers, `ITelemetrySensor` (connection handling like a live client).
- [ ] **P2 · M** `TelemetryDataReaderFactory`: dependency injection with discovery by assembly (Scrutor) instead of
  MEF, keeping the compatibility checks of the plugins.
- [ ] **P2 · M** Replace the last use of **Newtonsoft.Json** by System.Text.Json.
- [ ] **P2 · M** `SessionData`: the child objects reference their parent; a live timing view inherits from
  `SessionData`, so that the export and the live timing use one model, and the apps use `SessionData` instead of
  their own bindable views.
- [ ] **P2 · S** `SessionEvents`: a time of `TimeSpan?` rather than `TimeSpan.Zero` when a sector is missing.
- [ ] **P2 · L** **`GeolocationSingleSessionManager`**: injectable clock (`TimeProvider`), a delay per sector,
  several crossings ordered within one step, a filter of micro-movements, optional capture of the raw telemetry in the
  session, a thread-safe snapshot.
- [ ] **P3 · L** A simpler timing engine built on TPL Dataflow in place of `LapTimerService` (noted against an earlier engine: to
  re-evaluate against the current one).
- [ ] **P3 · XL** **Continuous recording** cut in chunks, the laps being created afterwards from the data (GPS,
  speed) and not from the time.
