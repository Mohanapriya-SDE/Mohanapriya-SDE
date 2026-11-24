using FluentValidation;
using FluentValidation.Results;
using KredFlow.Application.Common;
using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;
using KredFlow.Application.Payments;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;
using Microsoft.Extensions.Options;

namespace KredFlow.Application.Loans;

public class LoanService(
    ILoanRepository loans,
    IPaymentRepository payments,
    IPenaltyRepository penalties,
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    ILenderClock clock,
    IOptions<LoanOptions> options,
    TimeProvider timeProvider) : ILoanService
{
    private readonly LoanOptions _options = options.Value;

    public async Task<LoanResponse> CreateAsync(CreateLoanRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await customers.GetByIdAsync(currentUser.LenderId, request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");
        if (!customer.IsActive)
        {
            throw new BusinessRuleException("Loans cannot be created for an inactive customer.");
        }

        var today = await clock.TodayAsync(cancellationToken);
        ValidateStartDate(request.StartDate, today);

        var principal = request.PrincipalAmount;
        var emi = LoanCalculator.CalculateEmi(principal, request.AnnualInterestRate, request.TenureMonths);
        var rows = LoanCalculator.GenerateSchedule(principal, request.AnnualInterestRate, emi, request.TenureMonths, request.StartDate);
        EnsureScheduleIsConsistent(rows, principal, request.TenureMonths);

        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            LenderId = currentUser.LenderId,
            CustomerId = customer.Id,
            Principal = principal,
            AnnualInterestRate = request.AnnualInterestRate,
            TenureMonths = request.TenureMonths,
            EmiAmount = emi,
            StartDate = request.StartDate,
            Status = LoanStatus.Active,
            PrepaymentPolicy = request.PrepaymentPolicy ?? _options.DefaultPrepaymentPolicy,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        var schedule = rows.Select(r => ToEntity(loan.Id, r)).ToList();
        var disbursement = new LoanTransaction
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            TransactionType = TransactionType.Disbursement,
            Amount = principal,
            TransactionDate = loan.StartDate
        };

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            loans.Add(loan, schedule, disbursement);
            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return ToResponse(loan, scheduleGenerated: true);
    }

    public async Task<LoanResponse> GetAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var loan = await LoadAsync(loanId, cancellationToken);
        var withSchedule = await loans.GetLoanIdsWithScheduleAsync([loan.Id], cancellationToken);
        return ToResponse(loan, withSchedule.Contains(loan.Id));
    }

    public async Task<PagedResponse<LoanResponse>> ListAsync(LoanListQuery query, CancellationToken cancellationToken = default)
    {
        var (items, total) = await loans.ListAsync(currentUser.LenderId, query, cancellationToken);
        var withSchedule = await loans.GetLoanIdsWithScheduleAsync(items.Select(l => l.Id).ToList(), cancellationToken);

        return new PagedResponse<LoanResponse>
        {
            Items = items.Select(l => ToResponse(l, withSchedule.Contains(l.Id))).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total
        };
    }

    public async Task<IReadOnlyList<RepaymentScheduleResponse>> GetScheduleAsync(
        Guid loanId, DateOnly? asOfDate, CancellationToken cancellationToken = default)
    {
        var loan = await LoadAsync(loanId, cancellationToken);
        var asOf = asOfDate ?? await clock.TodayAsync(cancellationToken);
        var rows = await loans.GetCurrentScheduleAsync(loan.Id, cancellationToken);

        return rows.Select(ScheduleRowFigures.From).Select(r => new RepaymentScheduleResponse
        {
            InstallmentNumber = r.InstallmentNumber,
            DueDate = r.DueDate,
            OpeningPrincipal = r.OpeningPrincipal,
            PrincipalDue = r.PrincipalDue,
            InterestDue = r.InterestDue,
            TotalDue = r.TotalDue,
            PaidAmount = r.Paid,
            RemainingAmount = r.Remaining,
            PaymentStatus = LoanBalanceCalculator.StatusOf(r, asOf)
        }).ToList();
    }

    public async Task<LoanOutstandingResponse> GetOutstandingAsync(
        Guid loanId, DateOnly? asOfDate, CancellationToken cancellationToken = default)
    {
        var loan = await LoadAsync(loanId, cancellationToken);
        var asOf = asOfDate ?? await clock.TodayAsync(cancellationToken);
        var balance = await CalculateBalanceAsync(loan, asOf, cancellationToken);

        return new LoanOutstandingResponse
        {
            LoanId = loan.Id,
            PrincipalOutstanding = balance.PrincipalOutstanding,
            AccruedUnpaidInterest = balance.AccruedUnpaidInterest,
            OverdueAmount = balance.OverdueAmount,
            TotalOutstanding = balance.TotalOutstanding,
            AsOfDate = balance.AsOfDate
        };
    }

    public async Task<LoanResponse> CloseAsync(Guid loanId, CloseLoanRequest request, CancellationToken cancellationToken = default)
    {
        var loan = await LoadAsync(loanId, cancellationToken);
        if (loan.Status == LoanStatus.Closed)
        {
            throw new ConflictException("The loan is already closed.");
        }

        var today = await clock.TodayAsync(cancellationToken);
        if (request.ClosureDate > today)
        {
            throw new BusinessRuleException("The closure date cannot be in the future.");
        }

        if (request.ClosureDate < loan.StartDate)
        {
            throw new BusinessRuleException("The closure date cannot be before the loan start date.");
        }

        var lastPayment = await loans.GetLastPaymentDateAsync(loan.Id, cancellationToken);
        if (lastPayment is not null && request.ClosureDate < lastPayment)
        {
            throw new BusinessRuleException($"The closure date cannot be before the last payment on {lastPayment:yyyy-MM-dd}.");
        }

        var balance = await CalculateBalanceAsync(loan, request.ClosureDate, cancellationToken);

        // Ignoring penalties only skips the check: the penalties, waivers and ledger entries stay exactly as they are.
        var penaltyOutstanding = _options.PenaltyClosurePolicy == PenaltyClosurePolicy.BlockUntilPaidOrWaived
            ? (await penalties.GetFiguresAsync(loan.Id, cancellationToken)).Sum(f => f.Outstanding)
            : 0m;
        if (balance.PrincipalOutstanding > 0 || balance.AccruedUnpaidInterest > 0 || penaltyOutstanding > 0)
        {
            throw new BusinessRuleException(
                $"The loan still has dues: principal {balance.PrincipalOutstanding:0.00}, interest {balance.AccruedUnpaidInterest:0.00}, penalties {penaltyOutstanding:0.00}.");
        }

        loan.Status = LoanStatus.Closed;
        loan.ClosedDate = request.ClosureDate;
        loan.ClosureRemarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        loan.Version = Guid.NewGuid();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(loan, scheduleGenerated: true);
    }

    public async Task<PrepaymentResult> PrepayAsync(Guid loanId, PrepaymentRequest request, CancellationToken cancellationToken = default)
    {
        var loan = await LoadAsync(loanId, cancellationToken);

        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        var hash = RequestFingerprint.Compute(
            loan.Id, request.Amount, request.PaymentDate, request.Treatment, request.PaymentMethod, request.Remarks?.Trim());
        if (key is not null)
        {
            var existing = await payments.GetByIdempotencyKeyAsync(loan.Id, key, cancellationToken);
            if (existing is not null)
            {
                return Replay(existing, hash);
            }
        }

        if (loan.Status == LoanStatus.Closed)
        {
            throw new BusinessRuleException("A closed loan cannot be prepaid.");
        }

        var treatment = ResolveTreatment(loan.PrepaymentPolicy, request.Treatment);

        var today = await clock.TodayAsync(cancellationToken);
        if (request.PaymentDate > today)
        {
            throw new BusinessRuleException("The payment date cannot be in the future.");
        }

        if (request.PaymentDate < loan.StartDate)
        {
            throw new BusinessRuleException("The payment date cannot be before the loan start date.");
        }

        var lastPayment = await loans.GetLastPaymentDateAsync(loan.Id, cancellationToken);
        if (lastPayment is not null && request.PaymentDate < lastPayment)
        {
            throw new BusinessRuleException($"The payment date cannot be before the last payment on {lastPayment:yyyy-MM-dd}.");
        }

        var schedule = await loans.GetCurrentScheduleAsync(loan.Id, cancellationToken);
        var principalOutstanding = Math.Max(0m, loan.Principal - await loans.GetPrincipalPaidAsync(loan.Id, cancellationToken));
        var penaltyOutstanding = (await penalties.GetFiguresAsync(loan.Id, cancellationToken)).Sum(f => f.Outstanding);
        var plan = PaymentAllocationEngine.PlanPrepayment(
            schedule.Select(ScheduleRowFigures.From).ToList(), principalOutstanding, request.Amount, request.PaymentDate, penaltyOutstanding);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            PaymentMethod = request.PaymentMethod ?? PaymentMethod.Cash,
            Type = PaymentType.Prepayment,
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
            IdempotencyKey = key,
            RequestHash = key is null ? null : hash,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            CreatedByUserId = currentUser.UserId
        };
        var allocations = plan.Lines.Select(l => new PaymentAllocation
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            ScheduleId = l.ScheduleId,
            InterestAmount = l.Interest,
            PrincipalAmount = l.Principal,
            Type = l.Type
        }).ToList();
        var ledgerEntry = new LoanTransaction
        {
            Id = Guid.NewGuid(),
            LoanId = loan.Id,
            TransactionType = TransactionType.Repayment,
            Amount = request.Amount,
            TransactionDate = request.PaymentDate,
            ReferenceId = payment.Id
        };

        var replaced = schedule.Where(r => plan.ReplacedRowIds.Contains(r.Id)).ToList();
        var newRows = BuildRecast(loan, treatment, plan, replaced.Count);

        var previousEmi = loan.EmiAmount;
        var previousTenure = loan.TenureMonths;
        foreach (var row in replaced)
        {
            row.IsSuperseded = true;
            row.SupersededByPaymentId = payment.Id;
        }

        // A full payoff leaves no installments; keep the previous tenure rather than reporting a made-up one.
        var newTenure = plan.LastKeptInstallmentNumber + newRows.Count;
        if (newTenure > 0)
        {
            loan.TenureMonths = newTenure;
        }

        if (treatment == PrepaymentTreatment.ReduceEmi && newRows.Count > 0)
        {
            loan.EmiAmount = LoanCalculator.CalculateEmi(plan.NewPrincipalBalance, loan.AnnualInterestRate, newRows.Count);
        }

        loan.Version = Guid.NewGuid();

        var recast = new LoanRecast
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Treatment = treatment,
            PreviousEmiAmount = previousEmi,
            PreviousTenureMonths = previousTenure,
            NewEmiAmount = loan.EmiAmount,
            NewTenureMonths = loan.TenureMonths,
            PrincipalOutstandingAfter = plan.NewPrincipalBalance
        };

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // The payment and the superseded rows go first: the rows reference the payment, and the partial unique
                // index (current rows only) must see the old rows as superseded before their numbers are reused.
                payments.Add(payment, allocations, ledgerEntry, recast);
                await unitOfWork.SaveChangesAsync(ct);
                payments.AddRows(newRows.Select(r =>
                {
                    var row = ToEntity(loan.Id, r);
                    row.CreatedByPaymentId = payment.Id;
                    return row;
                }));
                await unitOfWork.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (ConflictException) when (key is not null)
        {
            var raced = await payments.GetByIdempotencyKeyAsync(loan.Id, key, cancellationToken);
            if (raced is not null)
            {
                return Replay(raced, hash);
            }

            throw;
        }

        return new PrepaymentResult(new PrepaymentResponse
        {
            PaymentId = payment.Id,
            LoanId = loan.Id,
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
            Treatment = treatment,
            InstallmentAmountSettled = plan.Lines.Where(l => l.Type == AllocationType.Installment).Sum(l => l.Total),
            PrincipalPrepaid = plan.PrincipalPrepaid,
            PrincipalOutstanding = plan.NewPrincipalBalance,
            EmiAmount = loan.EmiAmount,
            TenureMonths = loan.TenureMonths
        }, Replayed: false);
    }

    private static PrepaymentResult Replay(Payment existing, string hash)
    {
        if (existing.Type != PaymentType.Prepayment || existing.Recast is null || existing.RequestHash != hash)
        {
            throw new ConflictException("The idempotency key was already used for a different request.");
        }

        var recast = existing.Recast;
        return new PrepaymentResult(new PrepaymentResponse
        {
            PaymentId = existing.Id,
            LoanId = existing.LoanId,
            Amount = existing.Amount,
            PaymentDate = existing.PaymentDate,
            Treatment = recast.Treatment,
            InstallmentAmountSettled = existing.Allocations.Where(a => a.Type == AllocationType.Installment)
                .Sum(a => a.InterestAmount + a.PrincipalAmount),
            PrincipalPrepaid = existing.Allocations.Where(a => a.Type == AllocationType.PrincipalPrepayment).Sum(a => a.PrincipalAmount),
            PrincipalOutstanding = recast.PrincipalOutstandingAfter,
            EmiAmount = recast.NewEmiAmount,
            TenureMonths = recast.NewTenureMonths
        }, Replayed: true);
    }

    // ReduceTenure keeps the EMI and clears the balance sooner; ReduceEmi keeps the remaining count and lowers the EMI.
    private static List<ScheduleRow> BuildRecast(Loan loan, PrepaymentTreatment treatment, PrepaymentPlan plan, int replacedCount)
    {
        if (plan.NewPrincipalBalance <= 0)
        {
            return [];
        }

        var count = Math.Max(1, replacedCount);
        var emi = treatment == PrepaymentTreatment.ReduceEmi
            ? LoanCalculator.CalculateEmi(plan.NewPrincipalBalance, loan.AnnualInterestRate, count)
            : loan.EmiAmount;

        return LoanCalculator.GenerateSchedule(
            plan.NewPrincipalBalance, loan.AnnualInterestRate, emi, count, loan.StartDate, plan.LastKeptInstallmentNumber + 1);
    }

    private static PrepaymentTreatment ResolveTreatment(PrepaymentPolicy policy, PrepaymentTreatment? requested)
    {
        if (policy == PrepaymentPolicy.LenderChoice)
        {
            return requested ?? throw new ValidationException(
            [
                new ValidationFailure("Treatment", "'Treatment' is required because this loan's prepayment policy is LenderChoice.")
            ]);
        }

        var fixedTreatment = policy == PrepaymentPolicy.ReduceTenure ? PrepaymentTreatment.ReduceTenure : PrepaymentTreatment.ReduceEmi;
        if (requested is not null && requested != fixedTreatment)
        {
            throw new BusinessRuleException($"This loan's prepayment policy is {policy}; the treatment cannot be {requested}.");
        }

        return fixedTreatment;
    }

    private void ValidateStartDate(DateOnly startDate, DateOnly today)
    {
        if (startDate < today.AddDays(-_options.MaxBackdatedDays) || startDate > today.AddDays(_options.MaxFutureDays))
        {
            throw new ValidationException(
            [
                new ValidationFailure("StartDate",
                    $"'Start Date' must be between {today.AddDays(-_options.MaxBackdatedDays):yyyy-MM-dd} and {today.AddDays(_options.MaxFutureDays):yyyy-MM-dd}.")
            ]);
        }
    }

    // A failure here aborts creation before anything is saved.
    private static void EnsureScheduleIsConsistent(List<ScheduleRow> rows, decimal principal, int tenureMonths)
    {
        if (rows.Count != tenureMonths || rows.Sum(r => r.PrincipalDue) != principal || rows.Any(r => r.PrincipalDue <= 0))
        {
            throw new InvalidOperationException("The generated repayment schedule does not match the loan terms.");
        }
    }

    private async Task<LoanBalance> CalculateBalanceAsync(Loan loan, DateOnly asOf, CancellationToken cancellationToken)
    {
        var rows = await loans.GetCurrentScheduleAsync(loan.Id, cancellationToken);
        var principalPaid = await loans.GetPrincipalPaidAsync(loan.Id, cancellationToken);
        return LoanBalanceCalculator.Calculate(loan.Principal, principalPaid, rows.Select(ScheduleRowFigures.From), asOf);
    }

    // A loan owned by another lender is reported as not found so ids can't be probed across lenders.
    private async Task<Loan> LoadAsync(Guid loanId, CancellationToken cancellationToken) =>
        await loans.GetByIdAsync(currentUser.LenderId, loanId, cancellationToken)
        ?? throw new NotFoundException("Loan not found.");

    private static RepaymentSchedule ToEntity(Guid loanId, ScheduleRow row) => new()
    {
        Id = Guid.NewGuid(),
        LoanId = loanId,
        InstallmentNumber = row.InstallmentNumber,
        DueDate = row.DueDate,
        OpeningPrincipal = row.OpeningPrincipal,
        PrincipalDue = row.PrincipalDue,
        InterestDue = row.InterestDue,
        TotalDue = row.TotalDue
    };

    private static LoanResponse ToResponse(Loan loan, bool scheduleGenerated) => new()
    {
        Id = loan.Id,
        CustomerId = loan.CustomerId,
        PrincipalAmount = loan.Principal,
        AnnualInterestRate = loan.AnnualInterestRate,
        TenureMonths = loan.TenureMonths,
        EmiAmount = loan.EmiAmount,
        StartDate = loan.StartDate,
        Status = loan.Status,
        PrepaymentPolicy = loan.PrepaymentPolicy,
        ScheduleGenerated = scheduleGenerated,
        ClosedDate = loan.ClosedDate,
        CreatedAt = DateTime.SpecifyKind(loan.CreatedAt, DateTimeKind.Utc)
    };
}
