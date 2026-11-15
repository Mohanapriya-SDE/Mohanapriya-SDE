using FluentValidation;

namespace KredFlow.Application.Auth;

public class RegisterLenderRequestValidator : AbstractValidator<RegisterLenderRequest>
{
    public RegisterLenderRequestValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(255).EmailAddress();
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(p => PhoneNumberFormat.TryNormalize(p, out _))
            .WithMessage("'{PropertyName}' must be a valid Indian mobile number, optionally prefixed with +91.");
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength)
            .MaximumLength(PasswordPolicy.MaxLength)
            .Matches("[A-Z]").WithMessage("'{PropertyName}' must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("'{PropertyName}' must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("'{PropertyName}' must contain a digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("'{PropertyName}' must contain a symbol.");
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(255).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
    }
}
