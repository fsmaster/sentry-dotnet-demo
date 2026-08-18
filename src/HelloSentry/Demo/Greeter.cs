namespace HelloSentry.Demo;

/// <summary>The "hello world" half of the demo, plus one way to make it fail.</summary>
public static class Greeter
{
    public static string Greet(string name)
    {
        try
        {
            return $"Hello, {NameFormatter.Format(name)}!";
        }
        catch (Exception ex)
        {
            // Wrapping keeps the original frames as an inner exception, so the
            // Sentry event shows two stack traces from two different files.
            throw new InvalidOperationException($"Could not greet '{name}'.", ex);
        }
    }
}
