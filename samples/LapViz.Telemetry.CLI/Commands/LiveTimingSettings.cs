using Spectre.Console.Cli;
using System.ComponentModel;
using Spectre.Console;


namespace LapViz.Telemetry.CLI.Commands;
public sealed class LiveTimingSettings : CommandSettings
{
    [CommandOption("--hub|-h")]
    [Description("LiveTiming hub URL, for example https://lapviz.com/lt")]
    public string Hub { get; init; } = "https://lapviz.com/lt";

    [CommandOption("--session-id|-s")]
    [Description("Existing session id to join. If omitted, a new session is created.")]
    public string? SessionId { get; init; }

    [CommandOption("--test|-t")]
    [Description("Test by injecting session data.")]
    [DefaultValue(false)]
    public bool Test { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Hub))
            return ValidationResult.Error("Hub is required");
        if (!Uri.TryCreate(Hub, UriKind.Absolute, out _))
            return ValidationResult.Error("Hub must be a valid absolute URL");

        return ValidationResult.Success();
    }
}
