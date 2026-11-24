using KredFlow.Application.Common;
using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Loans;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;

namespace KredFlow.Application.Payments;

public class PaymentService(
    ILoanRepository loans,
    IPaymentRepository payments,
    IPenaltyRepository penalties,
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    ILenderClock clock,
    TimeProvider timeProvider) : IPaymentService
{
    public async Task<RecordPaymentResult> RecordAsync(Guid loanId, CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        var loan = await LoadLoanAsync(loanId, cancellationToken);
        var key = request.IdempotencyKey.Trim();
        var hash = RequestFingerprint.Compute(
            loan.Id, request.Amount, request.PaymentDate, request.PaymentMethod, request.ReferenceNumber?.Trim(), request.Remarks?.Trim());

        // A retry is answered from what was stored, even if the loan has been closed since.
        var existing = await payments.GetByIdempotencyKeyAsync(loan.Id, key, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, hash);
        }

        if (loan.Status == LoanStatus.Closed)
        {
            throw new BusinessRuleException("A closed loan cannot receive payments.");
        }

        await ValidateDateAsync(loan, request.PaymentDate, cancellationToken);

        var rows = await loans.GetCurrentScheduleAsync(loan.Id, cancellationToken);
        var penaltyFigures = await penalties.GetFiguresAsync(loan.Id, cancellationToken);
        var lines = PaymentAllocationEngine.AllocatePayment(
            rows.Select(ScheduleRowFigures.From).ToList(), penaltyFigures, request.Amount, request.PaymentDate);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            PaymentMethod = request.PaymentMethod,
            Type = PaymentType.Regular,
            ReferenceNumber = string.IsNullOrWhiteSpace(request.ReferenceNumber) ? null : request.ReferenceNumber.Trim(),
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            CreatedByUserId = currentUser.UserId
        };
        var allocations = lines.Select(l => new PaymentAllocation
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            ScheduleId = l.ScheduleId,
            PenaltyId = l.PenaltyId,
            Type = l.Type,
            InterestAmount = l.Interest,
            PrincipalAmount = l.Principal,
            PenaltyAmount = l.Penalty
        }).ToList();
        var ledgerEntry = new LoanTransaction
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            TransactionType = TransactionType.Repayment,
            Amount = payment.Amount,
            TransactionDate = payment.PaymentDate,
            ReferenceId = payment.Id
        };

        // Changing the version makes two simultaneous postings on one loan conflict instead of both succeeding.
        loan.Version = Guid.NewGuid();

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                payments.Add(payment, allocations, ledgerEntry);
                await unitOfWork.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (ConflictException)
        {
            // Two requests with the same key raced: the loser returns what the winner stored.
            var raced = await payments.GetByIdempotencyKeyAsync(loan.Id, key, cancellationToken);
            if (raced is not null)
            {
                return Replay(raced, hash);
            }

            throw;
        }

        var saved = await payments.GetDetailAsync(currentUser.LenderId, payment.Id, cancellationToken);
        return new RecordPaymentResult(PaymentMapper.ToResponse(saved!), Replayed: false);
    }

    public async Task<PaymentResponse> GetAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        PaymentMapper.ToResponse(await LoadPaymentAsync(paymentId, cancellationToken));

    public async Task<PagedResponse<PaymentSummaryResponse>> ListForLoanAsync(
        Guid loanId, PaymentListQuery query, CancellationToken cancellationToken = default)
    {
        var loan = await LoadLoanAsync(loanId, cancellationToken);
        var (items, total) = await payments.ListForLoanAsync(loan.Id, query, cancellationToken);
        return ToPage(items, total, query);
    }

    public async Task<PagedResponse<PaymentSummaryResponse>> ListForCustomerAsync(
        Guid customerId, PaymentListQuery query, CancellationToken cancellationToken = default)
    {
        var customer = await customers.GetByIdAsync(currentUser.LenderId, customerId, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");
        var (items, total) = await payments.ListForCustomerAsync(currentUser.LenderId, customer.Id, query, cancellationToken);
        return ToPage(items, total, query);
    }

    public async Task<PaymentResponse> ReverseAsync(Guid paymentId, ReversePaymentRequest request, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(paymentId, cancellationToken);
        var loan = payment.Loan;

        if (payment.Reversal is not null)
        {
            throw new ConflictException("The payment is already reversed.");
        }

        if (loan.Status == LoanStatus.Closed)
        {
            throw new BusinessRuleException("Payments of a closed loan cannot be reversed.");
        }

        var today = await clock.TodayAsync(cancellationToken);
        if (request.ReversalDate > today)
        {
            throw new BusinessRuleException("The reversal date cannot be in the future.");
        }

        if (request.ReversalDate < payment.PaymentDate)
        {
            throw new BusinessRuleException("The reversal date cannot be before the payment date.");
        }

        var later = await payments.GetActiveLaterPaymentsAsync(payment, cancellationToken);
        if (payment.Type == PaymentType.Prepayment && later.Count > 0)
        {
            throw new BusinessRuleException("A prepayment can only be reversed while no later payment exists. Reverse the later payments first.");
        }

        if (payment.Type == PaymentType.Regular && later.Any(p => p.Type == PaymentType.Prepayment))
        {
            throw new BusinessRuleException("A later prepayment depends on this payment. Reverse the prepayment first.");
        }

        if (payment.Type == PaymentType.Prepayment && payment.Recast is null)
        {
            throw new BusinessRuleException("This prepayment predates recast tracking and cannot be reversed.");
        }

        var reversal = new PaymentReversal
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Reason = request.Reason.Trim(),
            ReversalDate = request.ReversalDate,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            CreatedByUserId = currentUser.UserId
        };
        var ledgerEntry = new LoanTransaction
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            TransactionType = TransactionType.Reversal,
            Amount = payment.Amount,
            TransactionDate = request.ReversalDate,
            ReferenceId = payment.Id
        };

        loan.Version = Guid.NewGuid();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (payment.Type == PaymentType.Prepayment)
            {
                // Retire the rows the recast generated first, so that restoring the replaced rows reuses their numbers safely.
                var recastRows = await payments.GetRecastRowsAsync(payment.Id, ct);
                foreach (var row in recastRows.Where(r => r.CreatedByPaymentId == payment.Id))
                {
                    row.IsSuperseded = true;
                }

                await unitOfWork.SaveChangesAsync(ct);

                foreach (var row in recastRows.Where(r => r.SupersededByPaymentId == payment.Id))
                {
                    row.IsSuperseded = false;
                    row.SupersededByPaymentId = null;
                }

                loan.EmiAmount = payment.Recast!.PreviousEmiAmount;
                loan.TenureMonths = payment.Recast.PreviousTenureMonths;
            }

            payments.AddReversal(reversal, ledgerEntry);
            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return PaymentMapper.ToResponse((await payments.GetDetailAsync(currentUser.LenderId, payment.Id, cancellationToken))!);
    }

    private RecordPaymentResult Replay(Payment existing, string hash)
    {
        if (existing.RequestHash != hash)
        {
            throw new ConflictException("The idempotency key was already used for a different request.");
        }

        return new RecordPaymentResult(PaymentMapper.ToResponse(existing), Replayed: true);
    }

    private async Task ValidateDateAsync(Loan loan, DateOnly paymentDate, CancellationToken cancellationToken)
    {
        var today = await clock.TodayAsync(cancellationToken);
        if (paymentDate > today)
        {
            throw new BusinessRuleException("The payment date cannot be in the future.");
        }

        if (paymentDate < loan.StartDate)
        {
            throw new BusinessRuleException("The payment date cannot be before the loan start date.");
        }

        var lastPayment = await loans.GetLastPaymentDateAsync(loan.Id, cancellationToken);
        if (lastPayment is not null && paymentDate < lastPayment)
        {
            throw new BusinessRuleException($"The payment date cannot be before the last payment on {lastPayment:yyyy-MM-dd}.");
        }
    }

    private static PagedResponse<PaymentSummaryResponse> ToPage(IReadOnlyList<Payment> items, int total, PaymentListQuery query) => new()
    {
        Items = items.Select(PaymentMapper.ToSummary).ToList(),
        Page = query.Page,
        PageSize = query.PageSize,
        TotalCount = total
    };

    private async Task<Loan> LoadLoanAsync(Guid loanId, CancellationToken cancellationToken) =>
        await loans.GetByIdAsync(currentUser.LenderId, loanId, cancellationToken)
        ?? throw new NotFoundException("Loan not found.");

    // A payment of another lender's loan is reported as not found so ids can't be probed across lenders.
    private async Task<Payment> LoadPaymentAsync(Guid paymentId, CancellationToken cancellationToken) =>
        await payments.GetDetailAsync(currentUser.LenderId, paymentId, cancellationToken)
        ?? throw new NotFoundException("Payment not found.");
}
