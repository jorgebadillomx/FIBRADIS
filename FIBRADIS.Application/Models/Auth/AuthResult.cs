using System.Collections.Generic;

namespace FIBRADIS.Application.Models.Auth;

public sealed record AuthResult(TokenPair Tokens, IReadOnlyCollection<string> Roles);
