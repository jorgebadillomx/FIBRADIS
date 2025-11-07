namespace FIBRADIS.Application.Models.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken, long ExpiresInSeconds);
