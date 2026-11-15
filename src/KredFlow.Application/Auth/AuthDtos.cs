namespace KredFlow.Application.Auth;

public class RegisterLenderRequest
{
    public string BusinessName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterLenderResponse
{
    public Guid UserId { get; set; }
    public Guid LenderId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = "Registration successful";
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Access token lifetime in seconds.</summary>
    public int ExpiresIn { get; set; }

    public UserInfoResponse User { get; set; } = null!;
}

public class UserInfoResponse
{
    public Guid UserId { get; set; }
    public Guid LenderId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
}
