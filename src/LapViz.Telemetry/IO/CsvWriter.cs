using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.IO;

[Export(typeof(ITelemetryDataWriter))]
[PartCreationPolicy(CreationPolicy.NonShared)]
public class CsvWriter : ITelemetryDataWriter
{
    /// <summary>Writes from session data.</summary>
    /// <param name="driverSessionData">The driver session data.</param>
    /// <param name="output">The output.</param>
    /// <param name="overwrite">if set to <c>true</c> [overwrite].</param>
    public void WriteAll(DeviceSessionData driverSessionData, string output, bool overwrite)
    {
        using (var fs = new FileStream(output, FileMode.OpenOrCreate))
        {
            Stream stream = WriteAll(driverSessionData);
            stream.CopyTo(fs);
            fs.Flush();
        }
    }

    /// <summary>Gets the stream from session data.</summary>
    /// <param name="driverSessionData">The driver session data.</param>
    /// <returns>MemoryStream.</returns>
    public MemoryStream WriteAll(DeviceSessionData driverSessionData)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# LapViz Data v1.0 https://lapviz.com");
        builder.AppendLine("Timestamp," + string.Join(",", driverSessionData.TelemetryChannels));

        foreach (var lap in driverSessionData.Events.Where(x => x.Type == SessionEventType.Lap))
        {
            //if (lap.LapNumber == 0)
            //    continue;

            builder.AppendLine("#Lap " + lap.LapNumber + " " + lap.Time);
            foreach (var sector in driverSessionData.Events.Where(x => x.Type == SessionEventType.Sector && x.LapNumber == lap.LapNumber))
                builder.AppendLine("#Lap " + lap.LapNumber + " Sector " + sector.Sector + " " + sector.Time);

            var lapTelemetryData = driverSessionData.GetTelemetryDataForEvent(lap);

            foreach (var data in lapTelemetryData)
            {
                builder.Append(data.Timestamp.ToString("yyyy-MM-dd'T'HH:mm:ssK") + ",");

                var stringArray = Array.ConvertAll(data.Data.Select(x => x.Value).ToArray(), d => d.ToString("#.#####", CultureInfo.InvariantCulture));
                builder.AppendLine(string.Join(",", stringArray));
            }
        }

        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(builder.ToString());
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    public void WriteData(ITelemetryData geoTelemetryData)
    {
        throw new NotImplementedException();
    }

    public void WriteEvent(SessionDataEvent sessionDataEvent)
    {
        throw new NotImplementedException();
    }
}
