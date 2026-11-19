using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;

namespace KredFlow.Application.Common;

public class LenderClock(ILenderRepository lenders, ICurrentUserService currentUser, TimeProvider timeProvider) : ILenderClock
{
    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
    {
        var lender = await lenders.GetByIdAsync(currentUser.LenderId, cancellationToken)
            ?? throw new AuthenticationFailedException("Authentication is required.");

        var zone = TimeZoneInfo.FindSystemTimeZoneById(lender.TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
    }
}
