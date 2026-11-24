using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Loans;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;

namespace KredFlow.Application.Penalties;

public class PenaltyService(
    ILoanRepository loans,
    IPenaltyRepository penalties,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    ILenderClock clock,
    TimeProvider timeProvider) : IPenaltyService
{
    public async Task<IReadOnlyList<PenaltyResponse>> ListAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var loan = await LoadLoanAsync(loanId, cancellationToken);
        var figures = await penalties.GetFiguresAsync(loan.Id, cancellationToken);
        return figures.OrderBy(f => f.AssessedDate).ThenBy(f => f.Id).Select(f => ToResponse(loan.Id, f)).ToList();
    }

    public async Task<PenaltyResponse> AssessAsync(Guid loanId, AssessPenaltyRequest request, CancellationToken cancellationToken = default)
    {
        var loan = await LoadLoanAsync(loanId, cancellationToken);
        if (loan.Status == LoanStatus.Closed)
        {
            throw new BusinessRuleException("A closed loan cannot be assessed a penalty.");
        }

        var today = await clock.TodayAsync(cancellationToken);
        if (request.AssessedDate > today || request.AssessedDate < loan.StartDate)
        {
            throw new BusinessRuleException("The assessed date must be between the loan start date and today.");
        }

        Guid? scheduleId = null;
        if (request.InstallmentNumber is { } number)
        {
            var rows = await loans.GetCurrentScheduleAsync(loan.Id, cancellationToken);
            scheduleId = rows.FirstOrDefault(r => r.InstallmentNumber == number)?.Id
                ?? throw new BusinessRuleException($"The loan has no installment number {number}.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var penalty = new Penalty
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            ScheduleId = scheduleId,
            Amount = request.Amount,
            AssessedDate = request.AssessedDate,
            CreatedAt = now,
            CreatedByUserId = currentUser.UserId
        };
        var ledgerEntry = new LoanTransaction
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            TransactionType = TransactionType.Penalty,
            Amount = penalty.Amount,
            TransactionDate = penalty.AssessedDate,
            ReferenceId = penalty.Id
        };

        loan.Version = Guid.NewGuid();
        penalties.Add(penalty, ledgerEntry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(loan.Id, new PenaltyFigures(penalty.Id, request.InstallmentNumber, penalty.AssessedDate, penalty.Amount, 0m, 0m, now));
    }

    public async Task<PenaltyResponse> WaiveAsync(
        Guid loanId, Guid penaltyId, WaivePenaltyRequest request, CancellationToken cancellationToken = default)
    {
        var loan = await LoadLoanAsync(loanId, cancellationToken);
        var penalty = await penalties.GetByIdAsync(loan.Id, penaltyId, cancellationToken)
            ?? throw new NotFoundException("Penalty not found.");

        var figures = (await penalties.GetFiguresAsync(loan.Id, cancellationToken)).Single(f => f.Id == penalty.Id);
        if (request.Amount > figures.Outstanding)
        {
            throw new BusinessRuleException($"The waiver exceeds the outstanding penalty of {figures.Outstanding:0.00}.");
        }

        var waiver = new PenaltyWaiver
        {
            Id = Guid.NewGuid(),
            PenaltyId = penalty.Id,
            Amount = request.Amount,
            Reason = request.Reason.Trim(),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            CreatedByUserId = currentUser.UserId
        };
        var ledgerEntry = new LoanTransaction
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            TransactionType = TransactionType.Waiver,
            Amount = waiver.Amount,
            TransactionDate = await clock.TodayAsync(cancellationToken),
            ReferenceId = waiver.Id
        };

        loan.Version = Guid.NewGuid();
        penalties.AddWaiver(waiver, ledgerEntry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(loan.Id, figures with { Waived = figures.Waived + waiver.Amount });
    }

    private static PenaltyResponse ToResponse(Guid loanId, PenaltyFigures f) => new()
    {
        Id = f.Id,
        LoanId = loanId,
        InstallmentNumber = f.InstallmentNumber,
        AssessedDate = f.AssessedDate,
        Amount = f.Amount,
        PaidAmount = f.Paid,
        WaivedAmount = f.Waived,
        OutstandingAmount = f.Outstanding,
        CreatedAt = DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc)
    };

    private async Task<Loan> LoadLoanAsync(Guid loanId, CancellationToken cancellationToken) =>
        await loans.GetByIdAsync(currentUser.LenderId, loanId, cancellationToken)
        ?? throw new NotFoundException("Loan not found.");
}
