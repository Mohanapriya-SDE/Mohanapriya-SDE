namespace KredFlow.Application.Exceptions;

/// <summary>A well-formed request that breaks a business rule (HTTP 422).</summary>
public class BusinessRuleException(string message) : Exception(message);
