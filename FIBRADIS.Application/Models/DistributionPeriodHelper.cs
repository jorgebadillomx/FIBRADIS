using System;

namespace FIBRADIS.Application.Models;

public static class DistributionPeriodHelper
{
    public static string GetPeriodTag(DateTime payDate)
    {
        var quarter = ((payDate.Month - 1) / 3) + 1;
        return $"{quarter}T{payDate.Year}";
    }
}
