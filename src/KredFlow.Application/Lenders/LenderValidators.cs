using FluentValidation;
using KredFlow.Application.Auth;
using Microsoft.Extensions.Options;

namespace KredFlow.Application.Lenders;

public class UpdateLenderRequestValidator : AbstractValidator<UpdateLenderRequest>
{
    public UpdateLenderRequestValidator(IOptions<LenderOptions> options)
    {
        var supported = options.Value.SupportedTimeZones;

        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(p => PhoneNumberFormat.TryNormalize(p, out _))
            .WithMessage("'{PropertyName}' must be a valid Indian mobile number, optionally prefixed with +91.");
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.TimeZone)
            .NotEmpty()
            .Must(tz => supported.Contains(tz, StringComparer.Ordinal))
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", supported)}.");
    }
}
