using FluentValidation;
using KredFlow.Application.Auth;

namespace KredFlow.Application.Customers;

public class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(p => PhoneNumberFormat.TryNormalize(p, out _))
            .WithMessage("'{PropertyName}' must be a valid Indian mobile number, optionally prefixed with +91.");
        RuleFor(x => x.Address).MaximumLength(500);
    }
}

public class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(p => PhoneNumberFormat.TryNormalize(p, out _))
            .WithMessage("'{PropertyName}' must be a valid Indian mobile number, optionally prefixed with +91.");
        RuleFor(x => x.Address).MaximumLength(500);
    }
}

public class CustomerListQueryValidator : AbstractValidator<CustomerListQuery>
{
    public CustomerListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, CustomerListQuery.MaxPageSize);
    }
}
