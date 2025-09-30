using System.Reflection;
using LapViz.Telemetry.CLI.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LapViz.Telemetry.CLI;

internal class Program
{
    public static int Main(string[] args)
    {
        // Build DI container
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            logging.SetMinimumLevel(LogLevel.Information);
        });
        services.AddHttpClient();

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);
        app.Configure(cfg =>
        {
            cfg.SetApplicationName("lapviz");
            cfg.SetExceptionHandler((ex, _) =>
            {
                AnsiConsole.WriteException(
                    ex,
                    ExceptionFormats.ShortenPaths |
                    ExceptionFormats.ShortenTypes |
                    ExceptionFormats.ShortenMethods);
                return -1;
            });
            cfg.AddBranch("livetiming", branch =>
            {
                branch.AddCommand<LiveTimingCommand>("run")
                      .WithDescription("Connect to LiveTiming hub and render a live ranking table")
                      .WithExample(new[] { "livetiming", "run", "--hub", "https://lapviz.com/lt" });
                branch.SetDefaultCommand<LiveTimingCommand>();
            });

            cfg.AddBranch("laptimer", branch =>
            {
                branch.AddCommand<LaptimerCommand>("run")
                      .WithDescription("Console based laptimer, simulator or file replay, with optional LiveTiming session")
                      .WithExample(new[] { "laptimer", "run", "--input", "C:/path/session.lvz" })
                      .WithExample(new[] { "laptimer", "run", "--device-id", "12345", "--hub", "https://lapviz.com/lt" });
                branch.SetDefaultCommand<LaptimerCommand>();
            });
        });

        // If args were provided, run once and exit
        if (args.Length > 0)
        {
            return app.Run(args);
        }

        // Otherwise, start interactive REPL mode
        DisplayHeader();
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("LapViz");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("> ");
            Console.ForegroundColor = ConsoleColor.Green;
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var parts = ParseCommandLine(input);
            if (parts.Count == 0)
                continue;

            var cmd = parts[0].ToUpperInvariant();
            if (cmd == "QUIT" || cmd == "EXIT")
                break;

            if (cmd == "HELP")
            {
                app.Configure(cfg => { }); // ensures help is available
                app.Run(new[] { "--help" });
                continue;
            }

            try
            {
                app.Run(parts.ToArray());
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
            }
        }

        return 0;
    }

    private static void DisplayHeader()
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(@"    __                  _");
        Console.WriteLine(@"   / /  __ _ _ __/\   /(_)____");
        Console.WriteLine(@"  / /  / _` | '_ \ \ / / |_  /");
        Console.WriteLine(@" / /__| (_| | |_) \ V /| |/ /");
        Console.WriteLine(@" \____/\__,_| .__/ \_/ |_/___|");
        Console.WriteLine(@"            |_|");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(" Version {0}", Assembly.GetExecutingAssembly().GetName().Version);
        Console.ResetColor();
        Console.WriteLine();
    }

    public static List<string> ParseCommandLine(string cmdLine)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(cmdLine)) return args;

        var currentArg = new System.Text.StringBuilder();
        bool inQuotedArg = false;

        for (int i = 0; i < cmdLine.Length; i++)
        {
            if (cmdLine[i] == '"')
            {
                if (inQuotedArg)
                {
                    args.Add(currentArg.ToString());
                    currentArg = new System.Text.StringBuilder();
                    inQuotedArg = false;
                }
                else
                {
                    inQuotedArg = true;
                }
            }
            else if (cmdLine[i] == ' ')
            {
                if (inQuotedArg)
                {
                    currentArg.Append(cmdLine[i]);
                }
                else if (currentArg.Length > 0)
                {
                    args.Add(currentArg.ToString());
                    currentArg = new System.Text.StringBuilder();
                }
            }
            else
            {
                currentArg.Append(cmdLine[i]);
            }
        }

        if (currentArg.Length > 0) args.Add(currentArg.ToString());

        return args;
    }
}

file sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;
    public TypeRegistrar(IServiceCollection services) => _services = services;

    public ITypeResolver Build()
    {
        // Build a provider once Spectre finished registering its components
        var provider = _services.BuildServiceProvider();
        return new TypeResolver(provider);
    }

    public void Register(Type service, Type implementation)
        => _services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation)
        => _services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory)
        => _services.AddSingleton(service, _ => factory());
}

file sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public TypeResolver(ServiceProvider provider)
    {
        _provider = provider;
        _scope = _provider.CreateScope();
    }

    public object? Resolve(Type type)
        => _scope.ServiceProvider.GetService(type)
           ?? ActivatorUtilities.CreateInstance(_scope.ServiceProvider, type);

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

