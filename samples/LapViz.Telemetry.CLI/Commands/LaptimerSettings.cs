using System.ComponentModel;
using Spectre.Console.Cli;

namespace LapViz.Telemetry.CLI.Commands;
public sealed class LaptimerSettings : CommandSettings
{
    [CommandOption("--input|-i")]
    [Description("Path of the session file to replay as a race. If not set, simulator is used.")]
    public string? SessionFile { get; init; }

    [CommandOption("--session-id|-s")]
    [Description("Sets the session ID for live timing.")]
    public string? SessionId { get; init; }

    [CommandOption("--device-id|-d")]
    [Description("Sets the device ID for live timing.")]
    [DefaultValue("Default")]
    public string? DeviceId { get; init; }

    [CommandOption("--create|-c")]
    [Description("Specify if a session must be created.")]
    public bool CreateSession { get; init; }

    [CommandOption("--hub|-h")]
    [Description("LiveTiming hub URL, for example https://lapviz.com/lt")]
    public string Hub { get; init; } = "https://lapviz.com/lt";
}
