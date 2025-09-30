using System;
using System.Collections.Generic;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Abstractions;

public interface ITelemetryDataReader : IDisposable
{
    bool IsDataCompatible(string filename);
    ITelemetryDataReader Load(string filename);
    IList<string> GetTelemetryChannels();
    IList<DeviceSessionData> GetSessionData();
    string GetHash();
}
