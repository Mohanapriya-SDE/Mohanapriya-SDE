using FluentValidation;

namespace KredFlow.Application.Penalties;

public class AssessPenaltyRequestValidator : AbstractValidator<AssessPenaltyRequest>
{
    public AssessPenaltyRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m).LessThanOrEqualTo(10_000_000m).PrecisionScale(18, 2, false);
        RuleFor(x => x.AssessedDate).NotEqual(default(DateOnly)).WithMessage("'{PropertyName}' is required.");
        RuleFor(x => x.InstallmentNumber).GreaterThanOrEqualTo(1);
    }
}

public class WaivePenaltyRequestValidator : AbstractValidator<WaivePenaltyRequest>
{
    public WaivePenaltyRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m).PrecisionScale(18, 2, false);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
