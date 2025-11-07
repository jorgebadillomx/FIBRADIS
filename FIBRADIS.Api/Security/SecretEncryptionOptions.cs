using System.ComponentModel.DataAnnotations;

namespace FIBRADIS.Api.Security;

public sealed class SecretEncryptionOptions
{
    [Required]
    [MinLength(32)]
    public string MasterKey { get; set; } = string.Empty;
}
