using System;

namespace ClubTimerXbox.Services;

internal sealed class CashDayRevenueCache
{
    private (DateTime Day, long Payments, long Sessions)? _key;
    private int _total;

    public int Get(DateTime day, long paymentRevision, long sessionRevision, Func<int> calculate)
    {
        var key = (day.Date, paymentRevision, sessionRevision);
        if (_key == key) return _total;
        int total = calculate();
        _total = total;
        _key = key;
        return total;
    }
}
