using System.Text;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.Timing;
using LapViz.Telemetry.Timing.P3;

namespace LapViz.Telemetry.Tests.Timing;

public class PassingSourcesTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);

    private static string Snapshot(PassingTimingSession session, DateTimeOffset now) =>
        string.Join(";", session.Classify(now).Select(r =>
            $"{r.Position}:{r.Timing.Key}:{r.Timing.Laps}:{r.Timing.BestLapTime}:{r.GapTime}:{r.GapLaps}:{r.Timing.Finished}"));

    public sealed class P3Framing
    {
        [Fact]
        public void Crc_Is_Crc16_Ccitt_With_Initial_Value_FFFF()
        {
            var data = Encoding.ASCII.GetBytes("123456789");
            Assert.Equal(0x29B1, P3Codec.Crc(data, 0, data.Length));
        }

        [Fact]
        public void A_Passing_Survives_Encoding_With_Escaped_Bytes()
        {
            var passing = new P3Passing
            {
                PassingNumber = 0x8E,
                Transponder = 0x008F8D8E, // every byte that must be escaped
                RtcMicroseconds = P3Passing.MicrosecondsOf(T0),
                Strength = 141,
                Hits = 33,
                DecoderId = 0x000A8E01,
            };

            var bytes = P3Codec.Encode(passing.ToRecord());

            Assert.Equal(P3Codec.StartOfRecord, bytes[0]);
            Assert.Equal(P3Codec.EndOfRecord, bytes[bytes.Length - 1]);
            Assert.DoesNotContain(bytes.Skip(1).Take(bytes.Length - 2), b => b == P3Codec.StartOfRecord || b == P3Codec.EndOfRecord);

            var record = Assert.Single(new P3StreamDecoder().Push(bytes, 0, bytes.Length));
            var decoded = P3Passing.FromRecord(record)!;
            Assert.Equal(passing.PassingNumber, decoded.PassingNumber);
            Assert.Equal(passing.Transponder, decoded.Transponder);
            Assert.Equal(passing.RtcMicroseconds, decoded.RtcMicroseconds);
            Assert.Equal(passing.Strength, decoded.Strength);
            Assert.Equal(passing.Hits, decoded.Hits);
            Assert.Equal(passing.DecoderId, decoded.DecoderId);
            Assert.Equal(T0, P3Passing.TimeOf(decoded.RtcMicroseconds));
        }

        [Fact]
        public void A_Hand_Built_Frame_Is_Decoded()
        {
            // Header: SOR, version 2, length, CRC (zero for its computation), flags, TOR 0x0001; fields; EOR
            var raw = new List<byte> { 0x8E, 0x02, 0, 0, 0, 0, 0, 0, 0x01, 0x00 };
            raw.AddRange(new byte[] { 0x01, 4, 0x07, 0, 0, 0 });            // passing number 7
            raw.AddRange(new byte[] { 0x03, 4, 0x39, 0x30, 0, 0 });         // transponder 12345
            raw.AddRange(new byte[] { 0x04, 8, 0x40, 0x42, 0x0F, 0, 0, 0, 0, 0 }); // RTC 1 000 000 µs
            raw.AddRange(new byte[] { 0x81, 4, 0x01, 0x02, 0x03, 0x04 });   // decoder 0x04030201
            raw.Add(0x8F);
            var frame = raw.ToArray();
            frame[2] = (byte)frame.Length;
            var crc = P3Codec.Crc(frame, 0, frame.Length);
            frame[4] = (byte)crc;
            frame[5] = (byte)(crc >> 8);

            var passing = P3Passing.FromRecord(P3Codec.DecodeUnescaped(frame)!)!;

            Assert.Equal(7u, passing.PassingNumber);
            Assert.Equal(12345u, passing.Transponder);
            Assert.Equal(1_000_000ul, passing.RtcMicroseconds);
            Assert.Equal(0x04030201u, passing.DecoderId);
        }

        [Fact]
        public void The_Stream_Decoder_Handles_Chunks_Noise_And_Bad_Crc()
        {
            var good1 = P3Codec.Encode(new P3Passing { PassingNumber = 1, Transponder = 1001, RtcMicroseconds = 5 }.ToRecord());
            var good2 = P3Codec.Encode(new P3Passing { PassingNumber = 2, Transponder = 1002, RtcMicroseconds = 6 }.ToRecord());
            var bad = P3Codec.Encode(new P3Passing { PassingNumber = 3, Transponder = 1003, RtcMicroseconds = 7 }.ToRecord());
            bad[bad.Length - 2] ^= 0x01; // last data byte changed: wrong CRC

            var stream = new byte[] { 0x00, 0x13 }.Concat(good1).Concat(bad).Concat(new byte[] { 0x8E, 0x02 }).Concat(good2).ToArray();

            var decoder = new P3StreamDecoder();
            var records = new List<P3Record>();
            foreach (var b in stream)
                records.AddRange(decoder.Push(new[] { b }, 0, 1));

            Assert.Equal(new uint[] { 1001, 1002 }, records.Select(r => P3Passing.FromRecord(r)!.Transponder));
            Assert.True(decoder.Errors >= 3); // two noise bytes, the bad CRC, the record cut short

            var lenient = new P3StreamDecoder(verifyCrc: false).Push(bad, 0, bad.Length);
            Assert.False(Assert.Single(lenient).CrcValid);
        }

        [Fact]
        public void Status_Records_Are_Read()
        {
            var record = new P3Record(P3RecordTypes.Status, new[]
            {
                P3Field.Of(P3FieldTypes.Noise, 23, 2),
                P3Field.Of(P3FieldTypes.Temperature, unchecked((ushort)-5), 2),
                P3Field.Of(P3FieldTypes.LoopTriggers, 1, 1),
                P3Field.Of(P3FieldTypes.DecoderId, 9, 4),
            });

            var decoded = P3Codec.DecodeUnescaped(Unescape(P3Codec.Encode(record)))!;
            var status = P3Status.FromRecord(decoded)!;

            Assert.Equal((ushort)23, status.Noise);
            Assert.Equal((short)-5, status.Temperature);
            Assert.Equal((byte)1, status.LoopTriggers);
            Assert.Equal(9u, status.DecoderId);
            Assert.Null(P3Passing.FromRecord(decoded));
        }

        private static byte[] Unescape(byte[] bytes)
        {
            var result = new List<byte>();
            for (var i = 0; i < bytes.Length; i++)
                result.Add(bytes[i] == P3Codec.Escape ? (byte)(bytes[++i] - 0x20) : bytes[i]);
            return result.ToArray();
        }
    }

    public sealed class P3Mapping
    {
        [Fact]
        public void A_Passing_Gets_Its_Line_Key_Time_And_A_Stable_Id()
        {
            var mapper = new P3PassingMapper(decoder => decoder switch { 1u => 1, 2u => 2, _ => null });
            var p3 = new P3Passing { PassingNumber = 42, Transponder = 5501, RtcMicroseconds = P3Passing.MicrosecondsOf(T0), DecoderId = 2, Strength = 130 };

            var passing = mapper.Map(p3)!;

            Assert.Equal("5501", passing.Key);
            Assert.Equal(2, passing.Line);
            Assert.Equal(T0, passing.Timestamp);
            Assert.Equal(130, passing.Strength);
            Assert.Equal(passing.Id, mapper.Map(p3)!.Id);
            Assert.NotEqual(passing.Id, mapper.Map(new P3Passing { PassingNumber = 42, Transponder = 5501, RtcMicroseconds = p3.RtcMicroseconds + 1, DecoderId = 2 })!.Id);
            Assert.Null(mapper.Map(new P3Passing { Transponder = 1, DecoderId = 3 }));
        }

        [Fact]
        public void Utc_Time_Is_Preferred_Else_The_Decoder_Clock_Plus_The_Offset()
        {
            var mapper = P3PassingMapper.SingleLoop();
            mapper.RtcOffset = TimeSpan.FromHours(-2);
            var rtc = P3Passing.MicrosecondsOf(T0.AddHours(2));

            Assert.Equal(T0, mapper.Map(new P3Passing { Transponder = 1, RtcMicroseconds = rtc })!.Timestamp);
            Assert.Equal(T0.AddSeconds(1), mapper.Map(new P3Passing { Transponder = 1, RtcMicroseconds = rtc, UtcMicroseconds = P3Passing.MicrosecondsOf(T0.AddSeconds(1)) })!.Timestamp);
        }
    }

    public sealed class Simulator
    {
        private static PassingSimulator Field(int seed = 7) =>
            new PassingSimulator(new TimingLineLayout(3), seed)
                .AddField(8, TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(0.3), TimeSpan.FromSeconds(0.4));

        [Fact]
        public void Is_Deterministic_For_A_Seed_And_Ordered()
        {
            var a = Field().Generate(T0, TimeSpan.FromMinutes(5));
            var b = Field().Generate(T0, TimeSpan.FromMinutes(5));
            var c = Field(seed: 8).Generate(T0, TimeSpan.FromMinutes(5));

            Assert.Equal(a.Select(p => (p.Id, p.Key, p.Line, p.Timestamp)), b.Select(p => (p.Id, p.Key, p.Line, p.Timestamp)));
            Assert.NotEqual(a.Select(p => p.Timestamp), c.Select(p => p.Timestamp));
            Assert.True(a.Zip(a.Skip(1), (x, y) => x.Timestamp <= y.Timestamp).All(ok => ok));
            Assert.Equal(a.Count, a.Select(p => p.Id).Distinct().Count());
        }

        [Fact]
        public void A_Race_Of_Simulated_Passings_Is_Timed()
        {
            var passings = Field().Generate(T0, TimeSpan.FromMinutes(5));
            var session = new PassingTimingSession(new TimingLineLayout(3), TimingRules.Race(duration: TimeSpan.FromMinutes(5), startMode: TimingStartMode.FirstCrossing));
            session.Rules.MinLapTime = TimeSpan.FromSeconds(30);
            foreach (var passing in passings)
                session.AddPassing(passing);

            var rows = session.Classify(T0.AddMinutes(10));
            Assert.Equal(8, rows.Count);
            Assert.Equal("1001", rows[0].Timing.Key); // the fastest wins
            Assert.All(rows, r => Assert.True(r.Timing.Finished));
            Assert.All(rows, r => Assert.InRange(r.Timing.BestLapTime!.Value.TotalSeconds, 46, 52));
            Assert.All(rows, r => Assert.DoesNotContain(r.Timing.Rejected, x => x.Reason != PassingRejection.AfterFinish));
        }

        [Fact]
        public void Missed_And_Duplicated_Passings_Do_Not_Break_The_Lap_Count()
        {
            var sim = new PassingSimulator(new TimingLineLayout(3), seed: 3)
            {
                MissedPassingProbability = 0.05,
                DuplicateProbability = 0.1,
            }.AddField(5, TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.3));
            var clean = new PassingSimulator(new TimingLineLayout(3), seed: 3)
                .AddField(5, TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.3));

            var session = new PassingTimingSession(new TimingLineLayout(3), TimingRules.Practice());
            session.Rules.MinLapTime = TimeSpan.FromSeconds(30);
            foreach (var passing in sim.Generate(T0, TimeSpan.FromMinutes(10)))
                session.AddPassing(passing);

            var expectedLaps = clean.Generate(T0, TimeSpan.FromMinutes(10))
                .Where(p => p.Line == 1).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Count() - 1);

            foreach (var row in session.Classify(T0.AddMinutes(20)))
            {
                // A missed finish line is recovered from the intermediate lines, except when the start crossing or the
                // last finish line passing itself was missed
                Assert.InRange(row.Timing.Laps, expectedLaps[row.Timing.Key] - 2, expectedLaps[row.Timing.Key]);
                Assert.Contains(row.Timing.Rejected, r => r.Reason == PassingRejection.Duplicate);
            }
        }

        [Fact]
        public void Pit_Stops_Go_Through_The_Pit_Lines()
        {
            var layout = new TimingLineLayout(1, pitInLine: 10, pitOutLine: 11);
            var passings = new PassingSimulator(layout, seed: 5) { PitStopProbability = 0.2 }
                .AddField(3, TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.3))
                .Generate(T0, TimeSpan.FromMinutes(10));

            Assert.Contains(passings, p => p.Line == 10);
            Assert.Equal(passings.Count(p => p.Line == 10), passings.Count(p => p.Line == 11));
        }

        [Fact]
        public void Through_A_Fake_P3_Decoder_The_Result_Is_The_Same()
        {
            var layout = new TimingLineLayout(3);
            var passings = Field().Generate(T0, TimeSpan.FromMinutes(5));

            // One decoder per line, as P3 bytes on a stream read in chunks of 7 bytes
            uint? DecoderOfLine(int line) => (uint)(0x100 + line);
            var bytes = PassingSimulator.ToP3(passings, DecoderOfLine)
                .SelectMany(p => P3Codec.Encode(p.ToRecord()))
                .ToArray();
            var decoder = new P3StreamDecoder();
            var mapper = new P3PassingMapper(id => id.HasValue ? (int)id.Value - 0x100 : null);
            var viaP3 = new PassingTimingSession(layout, TimingRules.Practice());
            for (var i = 0; i < bytes.Length; i += 7)
            {
                foreach (var record in decoder.Push(bytes, i, Math.Min(7, bytes.Length - i)))
                    viaP3.AddPassing(mapper.Map(P3Passing.FromRecord(record)!)!);
            }

            var direct = new PassingTimingSession(layout, TimingRules.Practice());
            foreach (var passing in passings)
                direct.AddPassing(new TimingPassing { Id = passing.Id, Key = passing.Key, Line = passing.Line, Timestamp = TruncateToMicroseconds(passing.Timestamp) });

            Assert.Equal(0, decoder.Errors);
            Assert.Equal(Snapshot(direct, T0.AddMinutes(10)), Snapshot(viaP3, T0.AddMinutes(10)));
        }

        private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset t) => new DateTimeOffset(t.UtcTicks - (t.UtcTicks % 10), TimeSpan.Zero);
    }

    public sealed class Journal
    {
        [Fact]
        public void Every_Entry_Survives_A_Line_Of_Json()
        {
            var entries = new[]
            {
                TimingJournal.Session(new TimingLineLayout(3, 10, 11), TimingRules.Race(laps: 12, duration: TimeSpan.FromMinutes(15))),
                TimingJournal.Passing(new TimingPassing { Id = "a", Key = "1001", Line = 2, Timestamp = T0, Source = "D1", Strength = 120, Hits = 30, Manual = true }),
                TimingJournal.Start(T0),
                TimingJournal.Flag(RaceFlag.Yellow, T0.AddSeconds(5)),
                TimingJournal.Checkered(T0.AddMinutes(15)),
                TimingJournal.Invalidate("a"),
                TimingJournal.Restore("a"),
                TimingJournal.Competitor(new TimingCompetitor { Key = "1001", Number = "7", Name = "Alice", ClassName = "Senior", StartPosition = 3 }),
                TimingJournal.Penalty("1001", TimeSpan.FromSeconds(5), 1),
                TimingJournal.Status("1001", RaceEntryStatus.DSQ),
                TimingJournal.Status("1001", null),
            };

            foreach (var entry in entries)
            {
                var line = TimingJournal.Serialize(entry);
                Assert.DoesNotContain("\n", line);
                Assert.Equal(line, TimingJournal.Serialize(TimingJournal.Parse(line)!));
            }

            Assert.Contains("\"k\":\"Passing\"", TimingJournal.Serialize(entries[1]));
        }

        [Fact]
        public void Replaying_The_Journal_Rebuilds_The_Same_Session()
        {
            var layout = new TimingLineLayout(3);
            var rules = TimingRules.Race(duration: TimeSpan.FromMinutes(5), startMode: TimingStartMode.FirstCrossing);
            rules.MinLapTime = TimeSpan.FromSeconds(30);
            var live = new PassingTimingSession(layout, rules);
            var journal = new StringBuilder();

            void Record(TimingJournalEntry entry)
            {
                journal.AppendLine(TimingJournal.Serialize(entry));
                TimingJournal.Apply(entry, live);
            }

            journal.AppendLine(TimingJournal.Serialize(TimingJournal.Session(layout, rules)));
            Record(TimingJournal.Competitor(new TimingCompetitor { Key = "1001", Number = "7", Name = "Alice" }));
            Record(TimingJournal.Start(T0.AddSeconds(-30)));
            var passings = new PassingSimulator(layout, seed: 11).AddField(6, TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(0.5)).Generate(T0, TimeSpan.FromMinutes(5));
            foreach (var passing in passings)
                Record(TimingJournal.Passing(passing));
            Record(TimingJournal.Invalidate(passings[40].Id));
            Record(TimingJournal.Penalty("1002", TimeSpan.FromSeconds(10)));
            journal.AppendLine("{\"k\":\"SomethingNew\"}");
            journal.AppendLine("not json");

            var replayed = TimingJournal.Replay(new StringReader(journal.ToString()), out var skipped)!;

            Assert.Equal(2, skipped);
            Assert.Equal(Snapshot(live, T0.AddMinutes(10)), Snapshot(replayed, T0.AddMinutes(10)));
            Assert.Equal("Alice", replayed.GetTiming("1001")!.Competitor.Name);
            Assert.Equal(TimeSpan.FromSeconds(10), replayed.GetTiming("1002")!.PenaltyTime);
        }

        [Fact]
        public void A_Journal_Without_Header_Is_Not_Replayed()
        {
            var line = TimingJournal.Serialize(TimingJournal.Start(T0));
            Assert.Null(TimingJournal.Replay(new StringReader(line), out _));
        }
    }
}
