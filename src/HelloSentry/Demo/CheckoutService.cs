namespace HelloSentry.Demo;

/// <summary>
/// A miniature "business" call chain, so the stack trace in Sentry has a few
/// frames worth clicking through instead of just Main().
/// </summary>
public static class CheckoutService
{
    public static void Checkout(int itemCount, int totalCents)
    {
        SentrySdk.AddBreadcrumb(
            $"Checkout started: {itemCount} item(s), {totalCents} cents",
            category: "checkout");

        var unitPrice = PriceCalculator.UnitPrice(totalCents, itemCount);

        Console.WriteLine($"Unit price: {unitPrice} cents");
    }
}
