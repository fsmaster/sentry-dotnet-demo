namespace HelloSentry.Demo;

/// <summary>Turns a raw name into something printable - or refuses to.</summary>
internal static class NameFormatter
{
    public static string Format(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A greeting needs a name.", nameof(name));
        }

        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
