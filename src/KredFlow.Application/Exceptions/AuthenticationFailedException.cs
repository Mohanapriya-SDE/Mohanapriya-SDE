namespace KredFlow.Application.Exceptions;

/// <summary>Raised for any authentication failure; the message is deliberately generic.</summary>
public class AuthenticationFailedException(string message = "Invalid email or password.") : Exception(message);
