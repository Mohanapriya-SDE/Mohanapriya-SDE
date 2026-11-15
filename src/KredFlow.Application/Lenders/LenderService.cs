using KredFlow.Application.Auth;
using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;
using KredFlow.Domain.Entities;

namespace KredFlow.Application.Lenders;

public class LenderService(
    ILenderRepository lenders,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser) : ILenderService
{
    public async Task<LenderResponse> GetMeAsync(CancellationToken cancellationToken = default) =>
        ToResponse(await LoadCurrentAsync(cancellationToken));

    public async Task<LenderResponse> UpdateMeAsync(UpdateLenderRequest request, CancellationToken cancellationToken = default)
    {
        var lender = await LoadCurrentAsync(cancellationToken);

        lender.BusinessName = request.BusinessName.Trim();
        lender.PhoneNumber = NormalizePhone(request.PhoneNumber);
        lender.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        lender.TimeZone = request.TimeZone;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(lender);
    }

    public async Task DeactivateAsync(UpdateLenderStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (request.IsActive)
        {
            // An inactive lender can't authenticate, so reactivation can't be requested through the API.
            throw new BusinessRuleException("Reactivating a lender is not supported through the API.");
        }

        var lender = await LoadCurrentAsync(cancellationToken);
        if (await lenders.HasOpenLoansAsync(lender.Id, cancellationToken))
        {
            throw new BusinessRuleException("The lender still has active or overdue loans. Close or settle them before deactivating.");
        }

        lender.IsActive = false;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Lender> LoadCurrentAsync(CancellationToken cancellationToken) =>
        await lenders.GetByIdAsync(currentUser.LenderId, cancellationToken)
        ?? throw new AuthenticationFailedException("Authentication is required.");

    private static string NormalizePhone(string value) =>
        PhoneNumberFormat.TryNormalize(value, out var normalized)
            ? normalized
            : throw new BusinessRuleException("Invalid phone number.");

    private static LenderResponse ToResponse(Lender lender) => new()
    {
        Id = lender.Id,
        BusinessName = lender.BusinessName,
        OwnerName = lender.OwnerName,
        Email = lender.Email,
        PhoneNumber = lender.PhoneNumber,
        Address = lender.Address,
        TimeZone = lender.TimeZone,
        IsActive = lender.IsActive,
        // Stored as UTC; some providers return an unspecified kind, which would serialize without the trailing Z.
        CreatedAt = DateTime.SpecifyKind(lender.CreatedAt, DateTimeKind.Utc)
    };
}
