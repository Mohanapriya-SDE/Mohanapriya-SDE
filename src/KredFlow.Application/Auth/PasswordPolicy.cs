namespace KredFlow.Application.Auth;

/// <summary>Single source for password rules, shared by request validation and Identity options.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    // Upper bound stops very long inputs from burning CPU in the hasher.
    public const int MaxLength = 128;
}
