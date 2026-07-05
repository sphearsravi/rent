using System.Globalization;

namespace RentCollection.Web.Helpers;

public static class CurrencyHelper
{
    private static readonly CultureInfo KuwaitCulture = new CultureInfo("ar-KW");

    public static string FormatKWD(decimal? amount)
    {
        if (amount == null)
            return "KWD 0.000";

        return $"KWD {amount.Value:N3}";
    }

    public static string FormatKWD(decimal amount)
    {
        return $"KWD {amount:N3}";
    }
}
