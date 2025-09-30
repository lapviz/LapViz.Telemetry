using Konsole;

namespace LapViz.Telemetry.CLI.Commands;

public sealed class LaptimerKonsoleUi
{
    private IConsole _currentLapTime = null!;
    private IConsole _currentLapNumber = null!;
    private IConsole _bestLapTime = null!;
    private IConsole _lastLapTime = null!;
    private IConsole _lastSectorTime = null!;
    private IConsole _bestSectorTime = null!;
    private IConsole _statusWindow = null!;
    private IConsole _logWindow = null!;

    public void Initialize()
    {
        Console.Clear();

        // Current lap window
        var w = Window.OpenBox("Current Lap", 0, 0, 22, 5);
        _currentLapNumber = w.SplitLeft("Number");
        _currentLapTime = w.SplitRight("Time");

        // Last lap window
        var w2 = Window.OpenBox("Last Laps", 23, 0, 22, 5);
        _lastLapTime = w2.SplitLeft("Last");
        _bestLapTime = w2.SplitRight("Best");

        // Last Sectors window
        var w3 = Window.OpenBox("Last Sectors", 46, 0, 22, 5);
        _lastSectorTime = w3.SplitLeft("Last");
        _bestSectorTime = w3.SplitRight("Best");

        _statusWindow = Window.OpenBox("Status", 0, 5, 120, 4);
        _logWindow = Window.OpenBox("Logs", 0, 9, 120, 21);

        Console.CursorVisible = false;
    }

    public void UpdateCurrentLap(int number, string time)
    {
        _currentLapNumber.WriteLine(number.ToString());
        _currentLapTime.WriteLine(time);
    }

    public void UpdateLapTimes(string lastLap, ConsoleColor lastColor, string bestLap, ConsoleColor bestColor)
    {
        _lastLapTime.WriteLine(lastColor, lastLap);
        _bestLapTime.WriteLine(bestColor, bestLap);
    }

    public void UpdateSectors(string lastSector, ConsoleColor lastColor, string bestSector, ConsoleColor bestColor)
    {
        _lastSectorTime.WriteLine(lastColor, lastSector);
        _bestSectorTime.WriteLine(bestColor, bestSector);
    }

    public void UpdateStatus(string status) => _statusWindow.WriteLine(status);
    public void Log(string line) => _logWindow.WriteLine(line);

    public void Close()
    {
        Console.CursorVisible = true;
        Console.Clear();
    }
}
