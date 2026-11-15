namespace KredFlow.Application.Interfaces;

public interface ICurrentUserService
{
    /// <exception cref="Exceptions.AuthenticationFailedException">The caller has no valid user claim.</exception>
    Guid UserId { get; }

    /// <exception cref="Exceptions.AuthenticationFailedException">The caller has no valid lender claim.</exception>
    Guid LenderId { get; }
}
