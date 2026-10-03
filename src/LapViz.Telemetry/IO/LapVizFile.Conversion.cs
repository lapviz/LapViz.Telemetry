using System;
using System.Collections.Generic;
using System.Linq;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.IO;

/// <summary>Conversion between the LapViz format and the session model of the readers and writers.</summary>
public sealed partial class LapVizFile
{
    private const long TicksPerMicrosecond = 10;

    /// <summary>
    /// The session for the writers of the other formats: one <see cref="GeoTelemetryData"/> per sample (position, speed,
    /// altitude and accuracy taken from the common channels), the laps and sectors of <see cref="PreferredTiming"/> as events,
    /// the other timing sources in <see cref="DeviceSessionData.TimingData"/>.
    /// </summary>
    public DeviceSessionData ToDeviceSessionData()
    {
        var data = new DeviceSessionData(Session.Device?.Serial ?? string.Empty, string.Empty)
        {
            DriverDisplayName = Session.Driver,
            VehicleName = Session.Vehicle,
            DeviceBrand = Session.Device?.Brand,
            DeviceName = Session.Device?.Model,
            OriginalFilename = Session.SourceFile,
            CircuitCode = Session.Circuit?.Code ?? PreferredTiming?.Circuit?.Code,
            TelemetryChannels = _channels.Select(x => x.Name).ToList(),
        };

        var hasLatitude = TryGetValues("Latitude", out var latitude);
        var hasLongitude = TryGetValues("Longitude", out var longitude);
        var hasAltitude = TryGetValues("Altitude", out var altitude);
        var hasSpeed = TryGetValues("GPS Speed", out var speed) || TryGetValues("Speed", out speed);
        var hasAccuracy = TryGetValues("GPS Accuracy", out var accuracy) || TryGetValues("Accuracy", out accuracy);
        double maxSpeed = 0;
        for (int i = 0; i < Count; i++)
        {
            var sample = new GeoTelemetryData
            {
                Timestamp = TimestampAt(i),
                Data = _values.Select(x => double.IsNaN(x[i]) ? (double?)null : x[i]).ToList(),
            };

            if (hasLatitude && !double.IsNaN(latitude[i]))
                sample.Latitude = latitude[i];
            if (hasLongitude && !double.IsNaN(longitude[i]))
                sample.Longitude = longitude[i];
            if (hasAltitude && !double.IsNaN(altitude[i]))
                sample.Altitude = altitude[i];
            if (hasAccuracy && !double.IsNaN(accuracy[i]))
                sample.Accuracy = accuracy[i];
            if (hasSpeed && !double.IsNaN(speed[i]))
            {
                sample.Speed = speed[i];
                maxSpeed = Math.Max(maxSpeed, speed[i]);
            }

            data.TelemetryData.Add(sample);
        }

        data.MaxSpeed = maxSpeed;

        var preferred = PreferredTiming;
        if (preferred != null)
        {
            data.Generator = preferred.Source;
            data.IsTelemetryDevice = preferred.IsDevice;
            foreach (var timingEvent in Events(preferred))
                data.AddEvent(timingEvent);
        }
        else
        {
            data.Generator = Session.Device?.Brand ?? Generator?.Name;
        }

        foreach (var source in Timing.Where(x => !ReferenceEquals(x, preferred)))
        {
            var events = new SessionEvents { Generator = source.Source, IsTelemetryDevice = source.IsDevice };
            foreach (var timingEvent in Events(source))
                events.AddEvent(timingEvent);

            data.TimingData.Add(events);
        }

        return data;
    }

    /// <summary>
    /// A session read by any reader in the LapViz format (lossless: a channel is stored in float32 only when all its
    /// values are exact in float32). The events of the session become its timing source (the logger), the
    /// <see cref="DeviceSessionData.TimingData"/> the other sources. Samples going back in time are dropped.
    /// </summary>
    public static LapVizFile FromDeviceSessionData(DeviceSessionData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var samples = Increasing(data.TelemetryData ?? new List<ITelemetryData>());
        var events = data.Events?.ToList() ?? new List<SessionDataEvent>();
        var start = samples.Count > 0
            ? samples[0].Timestamp
            : events.Count > 0 ? events.Min(x => x.Timestamp - x.Time) : DateTimeOffset.UtcNow;

        // On a whole microsecond, so that the relative times keep the order of the samples
        start = start.AddTicks(-(start.UtcTicks % TicksPerMicrosecond));

        var file = new LapVizFile(start)
        {
            Session = new LapVizSessionInfo
            {
                Start = start,
                Driver = Empty(data.DriverDisplayName),
                Vehicle = Empty(data.VehicleName),
                SourceFile = Empty(data.OriginalFilename),
                Device = Empty(data.DeviceBrand) == null && Empty(data.DeviceName) == null && Empty(data.DeviceId) == null
                    ? null
                    : new LapVizDevice { Brand = Empty(data.DeviceBrand), Model = Empty(data.DeviceName), Serial = Empty(data.DeviceId) },
                Circuit = Circuit(data.CircuitConfiguration, data.CircuitCode),
            },
        };

        file.Time = samples.Select(x => (x.Timestamp - start).Ticks / TicksPerMicrosecond).ToArray();

        var names = data.TelemetryChannels ?? new List<string>();
        for (int k = 0; k < names.Count; k++)
        {
            var name = names[k];
            if (string.IsNullOrWhiteSpace(name) || file.IndexOf(name) >= 0)
                continue;

            var values = new double[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                var row = samples[i].Data;
                values[i] = row != null && k < row.Count && row[k].HasValue ? row[k].GetValueOrDefault() : double.NaN;
            }

            file.AddChannel(new LapVizChannel(name, null, ChooseType(name, values)), values);
        }

        // GPS samples without position columns: the position becomes channels
        if (samples.Count > 0 && file.IndexOf("Latitude") < 0 && file.IndexOf("Longitude") < 0 && samples.All(x => x is GeoTelemetryData))
        {
            file.AddChannel(new LapVizChannel("Latitude", "deg"), samples.Select(x => Position(((GeoTelemetryData)x).Latitude, ((GeoTelemetryData)x).Longitude, true)).ToArray());
            file.AddChannel(new LapVizChannel("Longitude", "deg"), samples.Select(x => Position(((GeoTelemetryData)x).Latitude, ((GeoTelemetryData)x).Longitude, false)).ToArray());
        }

        if (events.Any(x => (x.Type == SessionEventType.Lap || x.Type == SessionEventType.Sector) && x.Time > TimeSpan.Zero))
        {
            file.Timing.Add(TimingSource(
                Empty(data.Generator) ?? Empty(data.DeviceBrand) ?? "Logger",
                true,
                Circuit(data.CircuitConfiguration, data.CircuitCode),
                events,
                start));
        }

        foreach (var source in data.TimingData ?? new List<SessionEvents>())
        {
            var sourceEvents = source.Events?.ToList() ?? new List<SessionDataEvent>();
            if (sourceEvents.Any(x => (x.Type == SessionEventType.Lap || x.Type == SessionEventType.Sector) && x.Time > TimeSpan.Zero))
                file.Timing.Add(TimingSource(Empty(source.Generator) ?? "Timing", source.IsTelemetryDevice, Circuit(source.CircuitConfiguration, null), sourceEvents, start));
        }

        return file;
    }

    /// <summary>
    /// A timing source from lap and sector crossings (the timestamp of a crossing is the end of the lap or sector).
    /// Crossings of zero duration are not laps; sectors after the last lap make an incomplete lap.
    /// </summary>
    public static LapVizTimingSource TimingSource(string name, bool isDevice, LapVizCircuit? circuit, IEnumerable<SessionDataEvent> events, DateTimeOffset start)
    {
        var list = events.Where(x => (x.Type == SessionEventType.Lap || x.Type == SessionEventType.Sector) && x.Time > TimeSpan.Zero).ToList();
        var sectors = list.Where(x => x.Type == SessionEventType.Sector).ToLookup(x => x.LapNumber);
        var laps = list
            .Where(x => x.Type == SessionEventType.Lap)
            .GroupBy(x => x.LapNumber)
            .Select(x => x.OrderBy(e => e.Timestamp).Last())
            .Select(x => new LapVizLap
            {
                Number = x.LapNumber,
                Start = (x.Timestamp - x.Time - start).TotalSeconds,
                Duration = x.Time.TotalSeconds,
                Sectors = SectorTimes(sectors[x.LapNumber]),
            })
            .ToList();

        // Laps with sectors but no lap crossing: the session ended during the lap
        foreach (var partial in sectors.Where(x => !laps.Any(l => l.Number == x.Key)))
        {
            var first = partial.OrderBy(x => x.Sector).ThenBy(x => x.Timestamp).First();
            laps.Add(new LapVizLap
            {
                Number = partial.Key,
                Start = (first.Timestamp - first.Time - start).TotalSeconds,
                Sectors = SectorTimes(partial),
            });
        }

        return new LapVizTimingSource
        {
            Source = name,
            IsDevice = isDevice,
            Circuit = circuit,
            Laps = laps.OrderBy(x => x.Start).ToList(),
        };
    }

    private static List<double> SectorTimes(IEnumerable<SessionDataEvent> sectors) =>
        sectors
            .GroupBy(s => s.Sector)
            .OrderBy(s => s.Key)
            .Select(s => s.OrderBy(e => e.Timestamp).Last().Time.TotalSeconds)
            .ToList();

    /// <summary>Lap and sector crossings of a timing source.</summary>
    private IEnumerable<SessionDataEvent> Events(LapVizTimingSource source)
    {
        foreach (var lap in source.Laps)
        {
            var lapStart = Session.Start.AddTicks((long)Math.Round(lap.Start * TimeSpan.TicksPerSecond));
            var elapsed = TimeSpan.Zero;
            for (int sector = 0; sector < (lap.Sectors?.Count ?? 0); sector++)
            {
                var time = TimeSpan.FromTicks((long)Math.Round(lap.Sectors![sector] * TimeSpan.TicksPerSecond));
                elapsed += time;
                yield return new SessionDataEvent
                {
                    Type = SessionEventType.Sector,
                    LapNumber = lap.Number,
                    Sector = sector + 1,
                    Time = time,
                    Timestamp = lapStart + elapsed,
                    CircuitCode = source.Circuit?.Code,
                };
            }

            if (!lap.Duration.HasValue)
                continue;

            var duration = TimeSpan.FromTicks((long)Math.Round(lap.Duration.Value * TimeSpan.TicksPerSecond));
            yield return new SessionDataEvent
            {
                Type = SessionEventType.Lap,
                LapNumber = lap.Number,
                Time = duration,
                Timestamp = lapStart + duration,
                CircuitCode = source.Circuit?.Code,
            };
        }
    }

    /// <summary>Float64 for the positions and whenever a value is not exact in float32: the conversion is lossless.</summary>
    private static LapVizValueType ChooseType(string name, double[] values)
    {
        if (string.Equals(name, "Latitude", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Longitude", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Distance", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Altitude", StringComparison.OrdinalIgnoreCase))
        {
            return LapVizValueType.Float64;
        }

        foreach (var value in values)
        {
            if (!double.IsNaN(value) && (double)(float)value != value)
                return LapVizValueType.Float64;
        }

        return LapVizValueType.Float32;
    }

    private static List<ITelemetryData> Increasing(IEnumerable<ITelemetryData> samples)
    {
        var result = new List<ITelemetryData>();
        long? last = null;
        foreach (var sample in samples.OrderBy(x => x.Timestamp))
        {
            // The format has a microsecond resolution: two samples in the same microsecond are one
            var microseconds = sample.Timestamp.UtcTicks / TicksPerMicrosecond;
            if (last.HasValue && microseconds <= last.Value)
                continue;

            last = microseconds;
            result.Add(sample);
        }

        return result;
    }

    private static double Position(double latitude, double longitude, bool isLatitude) =>
        latitude == 0 && longitude == 0 ? double.NaN : isLatitude ? latitude : longitude;

    private static LapVizCircuit? Circuit(CircuitConfiguration? configuration, string? code)
    {
        if (configuration == null && string.IsNullOrEmpty(code))
            return null;

        return new LapVizCircuit
        {
            Code = Empty(configuration?.Code) ?? Empty(code),
            Name = Empty(configuration?.Name),
            CountryCode = Empty(configuration?.CountryCode),
        };
    }

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
