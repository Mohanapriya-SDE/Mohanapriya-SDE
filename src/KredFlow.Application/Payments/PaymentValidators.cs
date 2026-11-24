using FluentValidation;
using KredFlow.Domain.Enums;

namespace KredFlow.Application.Payments;

public class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m).PrecisionScale(18, 2, false);
        RuleFor(x => x.PaymentDate).NotEqual(default(DateOnly)).WithMessage("'{PropertyName}' is required.");
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.ReferenceNumber).MaximumLength(100);
        RuleFor(x => x.Remarks).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

public class ReversePaymentRequestValidator : AbstractValidator<ReversePaymentRequest>
{
    public ReversePaymentRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ReversalDate).NotEqual(default(DateOnly)).WithMessage("'{PropertyName}' is required.");
    }
}

public class PaymentListQueryValidator : AbstractValidator<PaymentListQuery>
{
    public PaymentListQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("'{PropertyName}' must not be before 'From'.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PaymentListQuery.MaxPageSize);
    }
}
