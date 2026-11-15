using FluentValidation;
using FluentValidation.Results;
using KredFlow.Application.Exceptions;
using KredFlow.Application.Interfaces;
using KredFlow.Domain.Constants;
using KredFlow.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace KredFlow.Application.Auth;

public class AuthService(
    UserManager<ApplicationUser> userManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    ILenderRepository lenders,
    IUnitOfWork unitOfWork,
    IJwtTokenService tokenService,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IAuthService
{
    public async Task<RegisterLenderResponse> RegisterAsync(
        RegisterLenderRequest request, CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);
        if (!PhoneNumberFormat.TryNormalize(request.PhoneNumber, out var phone))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.PhoneNumber), "Invalid phone number.")]);
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new ConflictException("Email is already registered.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lender = new Lender
        {
            Id = Guid.NewGuid(),
            BusinessName = request.BusinessName.Trim(),
            OwnerName = request.OwnerName.Trim(),
            Email = email,
            PhoneNumber = phone,
            TimeZone = Lender.DefaultTimeZone,
            IsActive = true,
            CreatedAt = now
        };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            LenderId = lender.Id,
            UserName = email,
            Email = email,
            PhoneNumber = phone,
            FullName = lender.OwnerName,
            IsActive = true,
            CreatedAt = now
        };

        // Creating the user saves the tracked lender in the same transaction; any failure rolls both back.
        await unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            lenders.Add(lender);
            ThrowIfFailed(await userManager.CreateAsync(user, request.Password));
            ThrowIfFailed(await userManager.AddToRoleAsync(user, RoleNames.Owner));
        }, cancellationToken);

        return new RegisterLenderResponse
        {
            UserId = user.Id,
            LenderId = lender.Id,
            BusinessName = lender.BusinessName,
            OwnerName = lender.OwnerName,
            Email = email
        };
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(NormalizeEmail(request.Email));
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            SpendHashingTime(request.Password);
            throw new AuthenticationFailedException();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            throw new AuthenticationFailedException();
        }

        await userManager.ResetAccessFailedCountAsync(user);

        // Activity is checked only after the password so inactive accounts look identical to wrong passwords.
        var lender = await lenders.GetByIdAsync(user.LenderId, cancellationToken);
        if (!user.IsActive || lender is null || !lender.IsActive)
        {
            throw new AuthenticationFailedException();
        }

        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.GenerateToken(user, lender, roles);

        return new LoginResponse
        {
            AccessToken = token.Value,
            ExpiresIn = token.ExpiresInSeconds,
            User = ToUserInfo(user, lender, roles)
        };
    }

    public async Task<UserInfoResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(currentUser.UserId.ToString());
        if (user is null || !user.IsActive || user.LenderId != currentUser.LenderId)
        {
            throw new AuthenticationFailedException();
        }

        var lender = await lenders.GetByIdAsync(user.LenderId, cancellationToken);
        if (lender is null || !lender.IsActive)
        {
            throw new AuthenticationFailedException();
        }

        return ToUserInfo(user, lender, await userManager.GetRolesAsync(user));
    }

    private static UserInfoResponse ToUserInfo(ApplicationUser user, Lender lender, IList<string> roles) => new()
    {
        UserId = user.Id,
        LenderId = user.LenderId,
        FullName = user.FullName,
        Email = user.Email!,
        Role = roles.FirstOrDefault() ?? RoleNames.Owner,
        BusinessName = lender.BusinessName
    };

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // Keeps failure timing close to a real password check so response time doesn't reveal whether an account exists.
    private void SpendHashingTime(string password) =>
        passwordHasher.HashPassword(new ApplicationUser(), password);

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                                       or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            throw new ConflictException("Email is already registered.");
        }

        throw new ValidationException(result.Errors.Select(e =>
            new ValidationFailure(e.Code.StartsWith("Password", StringComparison.Ordinal) ? "Password" : string.Empty, e.Description)));
    }
}
