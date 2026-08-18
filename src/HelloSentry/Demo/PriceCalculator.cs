namespace HelloSentry.Demo;

/// <summary>Where the bug lives: nobody checked for an empty basket.</summary>
internal static class PriceCalculator
{
    public static int UnitPrice(int totalCents, int itemCount)
    {
        // Integer division by zero - the classic. Sentry should point at this
        // exact line, in this exact file, on GitHub.
        return totalCents / itemCount;
    }
}
