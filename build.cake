var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

////////////////////////////////////////////////////////////////
// Tasks

Task("Clean")
    .Does(context =>
{
    context.CleanDirectory("./.artifacts");
});

Task("Build")
    .IsDependentOn("Clean")
    .Does(context =>
{
    Information("\nCompiling LapViz.Telemetry...");
    DotNetBuild("./src/LapViz.Telemetry/LapViz.Telemetry.csproj", new DotNetBuildSettings
    {
        Configuration = configuration,
        Verbosity = DotNetVerbosity.Minimal,
        NoLogo = true,
        NoIncremental = context.HasArgument("rebuild"),
        MSBuildSettings = new DotNetMSBuildSettings()
            //.TreatAllWarningsAs(MSBuildTreatAllWarningsAs.Error)
    });

    Information("\nCompiling LapViz.LiveTiming...");
    DotNetBuild("./src/LapViz.LiveTiming/LapViz.LiveTiming.csproj", new DotNetBuildSettings
    {
        Configuration = configuration,
        Verbosity = DotNetVerbosity.Minimal,
        NoLogo = true,
        NoIncremental = context.HasArgument("rebuild"),
        MSBuildSettings = new DotNetMSBuildSettings()
            //.TreatAllWarningsAs(MSBuildTreatAllWarningsAs.Error)
    });

    Information("\nCompiling LapViz.Telemetry.Tests...");
    DotNetBuild("./src/LapViz.Telemetry.Tests/LapViz.Telemetry.Tests.csproj", new DotNetBuildSettings
    {
        Configuration = configuration,
        Verbosity = DotNetVerbosity.Minimal,
        NoLogo = true,
        NoIncremental = context.HasArgument("rebuild"),
        MSBuildSettings = new DotNetMSBuildSettings()
            //.TreatAllWarningsAs(MSBuildTreatAllWarningsAs.Error)
    });
});

Task("Test")
    .IsDependentOn("Build")
    .Does(context => 
{
    DotNetTest("./src/LapViz.Telemetry.Tests/LapViz.Telemetry.Tests.csproj", new DotNetTestSettings {
        Configuration = configuration,
        Verbosity = DotNetVerbosity.Minimal,
        NoLogo = true,
        NoRestore = true,
        NoBuild = true,
    });
});

Task("Package")
    .IsDependentOn("Test")
    .Does(context =>
{
    // Pack Telemetry
    context.DotNetPack("./src/LapViz.Telemetry/LapViz.Telemetry.csproj", new DotNetPackSettings
    {
        Configuration = configuration,
        Verbosity = DotNetVerbosity.Minimal,
        NoLogo = true,
        NoRestore = true,
        NoBuild = false,
        OutputDirectory = "./.artifacts",
        MSBuildSettings = new DotNetMSBuildSettings()
            //.TreatAllWarningsAs(MSBuildTreatAllWarningsAs.Error)
    });

    // Pack LiveTiming
    context.DotNetPack("./src/LapViz.LiveTiming/LapViz.LiveTiming.csproj", new DotNetPackSettings
    {
        Configuration = configuration,
        Verbosity = DotNetVerbosity.Minimal,
        NoLogo = true,
        NoRestore = true,
        NoBuild = false,
        OutputDirectory = "./.artifacts",
        MSBuildSettings = new DotNetMSBuildSettings()
            //.TreatAllWarningsAs(MSBuildTreatAllWarningsAs.Error)
    });
});

Task("Publish-NuGet")
    .WithCriteria(ctx => BuildSystem.IsRunningOnGitHubActions, "Not running on GitHub Actions")
    .IsDependentOn("Package")
    .Does(context => 
{
    var apiKey = Argument<string>("nuget-key", null);
    if(string.IsNullOrWhiteSpace(apiKey)) {
        throw new CakeException("No NuGet API key was provided.");
    }

    // Push all generated packages (Telemetry + LiveTiming)
    foreach(var file in context.GetFiles("./.artifacts/*.nupkg")) 
    {
        context.Information("Publishing {0}...", file.GetFilename().FullPath);
        DotNetNuGetPush(file.FullPath, new DotNetNuGetPushSettings
        {
            Source = "https://api.nuget.org/v3/index.json",
            ApiKey = apiKey,
            SkipDuplicate = true,
        });
    }
});

////////////////////////////////////////////////////////////////
// Targets

Task("Publish")
    .IsDependentOn("Publish-NuGet");

Task("Default")
    .IsDependentOn("Package");

////////////////////////////////////////////////////////////////
// Execution

RunTarget(target);
