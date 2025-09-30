using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LapViz.LiveTiming.Models;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.CLI.Commands;
internal static class Extensions
{
    public static SessionDataDeviceDto ToSessionEventDto(this SessionDataEvent sessionDataEvent)
    {
        var newEventRequest = new SessionDataDeviceDto();
        newEventRequest.SessionId = sessionDataEvent.SessionId;
        newEventRequest.CircuitCode = sessionDataEvent.CircuitCode;
        newEventRequest.DeviceId = sessionDataEvent.DeviceId;
        newEventRequest.UserId = sessionDataEvent.UserId;
        newEventRequest.Events.Add(new SessionDeviceEventDto()
        {
            Time = sessionDataEvent.Time,
            Timestamp = sessionDataEvent.Timestamp,
            Factor = sessionDataEvent.Factor,
            LapNumber = sessionDataEvent.LapNumber,
            SectorNumber = sessionDataEvent.Sector,
            Type = sessionDataEvent.Type == SessionEventType.Lap ? SessionEventTypeDto.Lap : SessionEventTypeDto.Sector,
            Deleted = sessionDataEvent.Deleted
        });

        return newEventRequest;
    }
}
