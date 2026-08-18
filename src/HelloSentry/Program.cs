using System.Reflection;
using HelloSentry.Demo;
using Microsoft.Extensions.Configuration;

namespace HelloSentry;

/// <summary>
/// A deliberately small console app whose only job is to throw interesting
/// exceptions at a Sentry project, so you can verify that stack frames resolve
/// back to this repository's source on GitHub.
/// </summary>
public static class Program
{
    private static readonly Dictionary<string, string> SwitchMappings = new()
    {
        ["--dsn"] = "Sentry:Dsn",
        ["--environment"] = "Sentry:Environment",
        ["--release"] = "Sentry:Release",
    };

    public static int Main(string[] args)
    {
        // Anything that is not a "--switch value" pair is the command to run.
        var command = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "hello";
        var switches = args.SkipWhile(a => !a.StartsWith('-')).ToArray();

        var configuration = BuildConfiguration(switches);
        var options = BuildSentryOptions(configuration);

        Console.WriteLine($"HelloSentry {options.Release}");
        Console.WriteLine(string.IsNullOrWhiteSpace(options.Dsn)
            ? "No DSN configured - running with Sentry disabled. Set Sentry:Dsn in appsettings.json, SENTRY_DSN, or --dsn."
            : $"Sentry enabled  env={options.Environment}  dsn={Redact(options.Dsn)}");
        Console.WriteLine();

        // Init returns a disposable that flushes queued events on shutdown.
        using var _ = SentrySdk.Init(options);

        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("demo.command", command);
            scope.SetTag("demo.machine", Environment.MachineName);
        });

        return Run(command);
    }

    private static int Run(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "hello":
                Console.WriteLine(Greeter.Greet("world"));
                Console.WriteLine("Nothing was sent to Sentry. Try: crash | nested | message | unhandled");
                return 0;

            case "message":
                SentrySdk.AddBreadcrumb("Demo started", category: "demo");
                var id = SentrySdk.CaptureMessage("Hello from HelloSentry", SentryLevel.Info);
                Console.WriteLine($"Captured message  event_id={id}");
                return 0;

            case "crash":
                // A handled exception, three frames deep across three files.
                try
                {
                    CheckoutService.Checkout(itemCount: 0, totalCents: 4_999);
                }
                catch (Exception ex)
                {
                    var eventId = SentrySdk.CaptureException(ex);
                    Console.WriteLine($"Captured exception  event_id={eventId}");
                }
                return 0;

            case "nested":
                // An exception chain: the inner frames live in a different file
                // than the outer ones, which is the interesting case for
                // checking that every frame links to GitHub.
                try
                {
                    Greeter.Greet(name: "");
                }
                catch (Exception ex)
                {
                    var eventId = SentrySdk.CaptureException(ex);
                    Console.WriteLine($"Captured nested exception  event_id={eventId}");
                }
                return 0;

            case "unhandled":
                // Not caught anywhere: the SDK's unhandled-exception hook reports
                // it, then the runtime terminates the process with a non-zero code.
                Console.WriteLine("Throwing an unhandled exception...");
                CheckoutService.Checkout(itemCount: 0, totalCents: 1_200);
                return 0;

            default:
                Console.Error.WriteLine($"Unknown command '{command}'.");
                Console.Error.WriteLine("Commands: hello | message | crash | nested | unhandled");
                return 2;
        }
    }

    private static IConfiguration BuildConfiguration(string[] switches)
    {
        var environmentName =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("SENTRY_ENVIRONMENT");

        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            builder.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false);
        }

        // Sentry__Dsn=... style overrides, then command-line switches. Last wins.
        return builder
            .AddEnvironmentVariables()
            .AddCommandLine(switches, SwitchMappings)
            .Build();
    }

    private static SentryOptions BuildSentryOptions(IConfiguration configuration)
    {
        var options = new SentryOptions();
        configuration.GetSection("Sentry").Bind(options);

        // Plain SENTRY_* variables win over the config file - handy in CI and in
        // containers, where nobody wants to rewrite appsettings.json.
        var dsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
        if (!string.IsNullOrWhiteSpace(dsn))
        {
            options.Dsn = dsn;
        }

        // The release name is the hinge of the whole demo: Sentry matches the
        // event's release to the commits associated with that release, and that
        // is how a stack frame turns into a GitHub link. Source Link stamps the
        // commit SHA into the informational version at build time, so this ends
        // up looking like "HelloSentry@1.0.0+9f2c1ab".
        options.Release ??= $"HelloSentry@{InformationalVersion()}";

        // Report exceptions that nobody caught, and keep the console readable.
        options.AutoSessionTracking = true;
        options.StackTraceMode = StackTraceMode.Enhanced;

        return options;
    }

    private static string InformationalVersion() =>
        typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";

    private static string Redact(string dsn)
    {
        // https://<key>@o0.ingest.sentry.io/1234  ->  https://***@o0.ingest.sentry.io/1234
        var at = dsn.IndexOf('@');
        var scheme = dsn.IndexOf("//", StringComparison.Ordinal);
        return at > 0 && scheme > 0 ? string.Concat(dsn.AsSpan(0, scheme + 2), "***", dsn.AsSpan(at)) : dsn;
    }
}
