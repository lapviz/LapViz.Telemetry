using System.IO;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Abstractions;

public interface ITelemetryDataWriter
{
    void WriteEvent(SessionDataEvent sessionDataEvent);
    void WriteData(ITelemetryData geoTelemetryData);
    void WriteAll(DeviceSessionData driverSessionData, string output, bool overwrite);
    MemoryStream WriteAll(DeviceSessionData driverSessionData);
}
