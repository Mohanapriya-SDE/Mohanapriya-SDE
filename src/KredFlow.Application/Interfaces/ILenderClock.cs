namespace KredFlow.Application.Interfaces;

/// <summary>Business dates are evaluated in the caller's lender time zone.</summary>
public interface ILenderClock
{
    Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default);
}
