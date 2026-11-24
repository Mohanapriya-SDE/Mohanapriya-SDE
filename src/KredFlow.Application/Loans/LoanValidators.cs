using FluentValidation;
using KredFlow.Domain.Enums;
using Microsoft.Extensions.Options;

namespace KredFlow.Application.Loans;

public class CreateLoanRequestValidator : AbstractValidator<CreateLoanRequest>
{
    public CreateLoanRequestValidator(IOptions<LoanOptions> options)
    {
        var o = options.Value;

        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.PrincipalAmount)
            .InclusiveBetween(o.MinPrincipal, o.MaxPrincipal)
            .PrecisionScale(18, 2, false);
        RuleFor(x => x.AnnualInterestRate)
            .InclusiveBetween(o.MinAnnualInterestRate, o.MaxAnnualInterestRate)
            .PrecisionScale(5, 2, false);
        RuleFor(x => x.TenureMonths).InclusiveBetween(o.MinTenureMonths, o.MaxTenureMonths);
        RuleFor(x => x.StartDate).NotEqual(default(DateOnly)).WithMessage("'{PropertyName}' is required.");
        RuleFor(x => x.PrepaymentPolicy).IsInEnum();
    }
}

public class LoanListQueryValidator : AbstractValidator<LoanListQuery>
{
    public LoanListQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.MinPrincipal).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.MaxPrincipal).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.MaxPrincipal)
            .GreaterThanOrEqualTo(x => x.MinPrincipal!.Value)
            .When(x => x.MinPrincipal is not null && x.MaxPrincipal is not null)
            .WithMessage("'{PropertyName}' must not be less than 'Min Principal'.");
        RuleFor(x => x.StartDateTo)
            .GreaterThanOrEqualTo(x => x.StartDateFrom!.Value)
            .When(x => x.StartDateFrom is not null && x.StartDateTo is not null)
            .WithMessage("'{PropertyName}' must not be before 'Start Date From'.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, LoanListQuery.MaxPageSize);
    }
}

public class CloseLoanRequestValidator : AbstractValidator<CloseLoanRequest>
{
    public CloseLoanRequestValidator()
    {
        RuleFor(x => x.ClosureDate).NotEqual(default(DateOnly)).WithMessage("'{PropertyName}' is required.");
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

public class PrepaymentRequestValidator : AbstractValidator<PrepaymentRequest>
{
    public PrepaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m).PrecisionScale(18, 2, false);
        RuleFor(x => x.PaymentDate).NotEqual(default(DateOnly)).WithMessage("'{PropertyName}' is required.");
        RuleFor(x => x.Treatment).IsInEnum();
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.Remarks).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).MaximumLength(100);
    }
}
