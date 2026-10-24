using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LapViz.Telemetry.Timing.P3;

namespace LapViz.Telemetry.Timing;

/// <summary>A competitor of the <see cref="PassingSimulator"/>.</summary>
public sealed class SimulatedCompetitor
{
    /// <summary>Key of its passings (transponder number).</summary>
    public string Key { get; set; } = "";

    /// <summary>Mean lap time.</summary>
    public TimeSpan MeanLapTime { get; set; } = TimeSpan.FromSeconds(50);

    /// <summary>Standard deviation of the lap time.</summary>
    public TimeSpan LapTimeSpread { get; set; } = TimeSpan.FromSeconds(0.4);
}

/// <summary>
/// Generates the passings of a field of competitors on a layout: lap times around a mean, sector lines, pit stops,
/// missed and duplicated passings. Deterministic for a seed, so tests, demos and the fake decoder replay the same
/// session. Not thread-safe.
/// </summary>
public sealed class PassingSimulator
{
    private readonly TimingLineLayout _layout;
    private readonly int _seed;

    /// <summary>Creates a simulator on <paramref name="layout"/>.</summary>
    public PassingSimulator(TimingLineLayout layout, int seed = 1)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _seed = seed;
    }

    /// <summary>The competitors, in grid order (the first crosses the line first).</summary>
    public IList<SimulatedCompetitor> Competitors { get; } = new List<SimulatedCompetitor>();

    /// <summary>Time between two competitors at their first passing.</summary>
    public TimeSpan GridInterval { get; set; } = TimeSpan.FromSeconds(0.6);

    /// <summary>Probability that a decoder misses a passing (each line, each lap).</summary>
    public double MissedPassingProbability { get; set; }

    /// <summary>Probability that a passing is seen twice (second antenna, repeat), within 1.5 s.</summary>
    public double DuplicateProbability { get; set; }

    /// <summary>Probability of a pit stop on a lap, when the layout has pit lines.</summary>
    public double PitStopProbability { get; set; }

    /// <summary>Time lost in a pit stop.</summary>
    public TimeSpan PitStopTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Adds <paramref name="count"/> competitors, transponders from <paramref name="firstTransponder"/>, the first one
    /// the fastest, each next one <paramref name="step"/> slower.
    /// </summary>
    public PassingSimulator AddField(int count, TimeSpan baseLapTime, TimeSpan step, TimeSpan spread, int firstTransponder = 1001)
    {
        for (var i = 0; i < count; i++)
        {
            Competitors.Add(new SimulatedCompetitor
            {
                Key = (firstTransponder + i).ToString(CultureInfo.InvariantCulture),
                MeanLapTime = baseLapTime + TimeSpan.FromTicks(step.Ticks * i),
                LapTimeSpread = spread,
            });
        }

        return this;
    }

    /// <summary>
    /// The passings from <paramref name="start"/> (first crossing of the first competitor) until one lap of the slowest
    /// competitor after <paramref name="duration"/>, so that everyone crosses the line after a checkered flag shown at
    /// the end of the duration or at the leader's next passing; in time order, ids <c>sim:1</c>, <c>sim:2</c>…
    /// </summary>
    public IReadOnlyList<TimingPassing> Generate(DateTimeOffset start, TimeSpan duration)
    {
        var random = new Random(_seed);
        var end = start + duration + (Competitors.Count == 0 ? TimeSpan.Zero : Competitors.Max(c => c.MeanLapTime));
        var passings = new List<TimingPassing>();
        var lines = _layout.SectorLines;
        var hasPit = _layout.PitInLine.HasValue && _layout.PitOutLine.HasValue;

        for (var c = 0; c < Competitors.Count; c++)
        {
            var competitor = Competitors[c];
            var t = start + TimeSpan.FromTicks(GridInterval.Ticks * c);
            Emit(passings, random, competitor.Key, TimingLineLayout.FinishLine, t);

            while (t <= end)
            {
                var lapTime = Gaussian(random, competitor.MeanLapTime, competitor.LapTimeSpread);

                // Intermediate lines at shares of the lap, with a little noise
                if (lines > 1)
                {
                    var shares = Enumerable.Range(0, lines).Select(_ => 1 + ((random.NextDouble() - 0.5) * 0.06)).ToArray();
                    var total = shares.Sum();
                    var elapsed = 0.0;
                    for (var line = 2; line <= lines; line++)
                    {
                        elapsed += shares[line - 2] / total;
                        Emit(passings, random, competitor.Key, line, t + TimeSpan.FromTicks((long)(lapTime.Ticks * elapsed)));
                    }
                }

                if (hasPit && random.NextDouble() < PitStopProbability)
                {
                    var pitIn = t + TimeSpan.FromTicks((long)(lapTime.Ticks * 0.9));
                    Emit(passings, random, competitor.Key, _layout.PitInLine!.Value, pitIn);
                    Emit(passings, random, competitor.Key, _layout.PitOutLine!.Value, pitIn + TimeSpan.FromTicks((long)(PitStopTime.Ticks * 0.9)));
                    lapTime += PitStopTime;
                }

                t += lapTime;
                Emit(passings, random, competitor.Key, TimingLineLayout.FinishLine, t);
            }
        }

        passings.Sort((a, b) =>
        {
            var c = a.Timestamp.CompareTo(b.Timestamp);
            return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
        });
        for (var i = 0; i < passings.Count; i++)
            passings[i].Id = "sim:" + (i + 1).ToString(CultureInfo.InvariantCulture);
        return passings;
    }

    /// <summary>
    /// The passings as a P3 decoder would send them: one decoder per line (<paramref name="decoderOfLine"/>), passing
    /// numbers counted per decoder, decoder clock in UTC.
    /// </summary>
    public static IReadOnlyList<P3Passing> ToP3(IEnumerable<TimingPassing> passings, Func<int, uint?> decoderOfLine)
    {
        if (passings == null) throw new ArgumentNullException(nameof(passings));
        if (decoderOfLine == null) throw new ArgumentNullException(nameof(decoderOfLine));

        var numbers = new Dictionary<uint, uint>();
        var result = new List<P3Passing>();
        foreach (var passing in passings)
        {
            var decoder = decoderOfLine(passing.Line);
            var key = decoder ?? 0;
            numbers[key] = numbers.TryGetValue(key, out var n) ? n + 1 : 1;
            result.Add(new P3Passing
            {
                PassingNumber = numbers[key],
                Transponder = uint.Parse(passing.Key, NumberStyles.Integer, CultureInfo.InvariantCulture),
                RtcMicroseconds = P3Passing.MicrosecondsOf(passing.Timestamp),
                Strength = (ushort)(passing.Strength ?? 120),
                Hits = (ushort)(passing.Hits ?? 30),
                DecoderId = decoder,
            });
        }

        return result;
    }

    private void Emit(List<TimingPassing> passings, Random random, string key, int line, DateTimeOffset at)
    {
        if (random.NextDouble() < MissedPassingProbability)
            return;

        passings.Add(new TimingPassing { Key = key, Line = line, Timestamp = at, Strength = 100 + random.Next(60), Hits = 20 + random.Next(30) });

        if (random.NextDouble() < DuplicateProbability)
            passings.Add(new TimingPassing { Key = key, Line = line, Timestamp = at + TimeSpan.FromMilliseconds(200 + random.Next(1300)) });
    }

    // Box-Muller, bounded to three deviations so that a lap is never absurd
    private static TimeSpan Gaussian(Random random, TimeSpan mean, TimeSpan spread)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        z = Math.Max(-3, Math.Min(3, z));
        return mean + TimeSpan.FromTicks((long)(spread.Ticks * z));
    }
}
