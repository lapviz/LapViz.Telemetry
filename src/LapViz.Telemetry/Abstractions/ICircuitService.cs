using System;
using System.Threading.Tasks;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Abstractions;

public interface ICircuitService
{
    Task<CircuitConfiguration> GetByCode(string code);
    Task<CircuitConfiguration> Detect(GeoTelemetryData geoLocation);
    Task Sync(double lat, double lon, int radius);
    event EventHandler<CircuitSyncProgress> SyncProgress;
}
