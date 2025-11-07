using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Auth;
using Microsoft.Extensions.Options;

namespace FIBRADIS.Api.Security;

public sealed class AesSecretService : ISecretService
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int KeySizeBits = 256;
    private const int Iterations = 100_000;

    private readonly ConcurrentDictionary<string, UserSecret> _secrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly SecretEncryptionOptions _options;
    private readonly ISecurityMetricsRecorder _metrics;

    public AesSecretService(IOptions<SecretEncryptionOptions> options, ISecurityMetricsRecorder metrics)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        if (string.IsNullOrWhiteSpace(_options.MasterKey))
        {
            throw new InvalidOperationException("Secret encryption master key must be configured");
        }
    }

    public Task StoreAsync(string userId, string provider, string plainTextKey, CancellationToken cancellationToken)
    {
        var encrypted = Encrypt(userId, provider, plainTextKey);
        var secret = new UserSecret
        {
            UserId = userId,
            Provider = provider,
            EncryptedKey = encrypted,
            CreatedAtUtc = DateTime.UtcNow,
            LastUsedAtUtc = null
        };

        var key = ComposeKey(userId, provider);
        _secrets[key] = secret;
        _metrics.RecordByokKeyActive();
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string userId, string provider, CancellationToken cancellationToken)
    {
        var key = ComposeKey(userId, provider);
        if (_secrets.TryGetValue(key, out var secret))
        {
            var plain = Decrypt(userId, provider, secret.EncryptedKey);
            _secrets[key] = secret with { LastUsedAtUtc = DateTime.UtcNow };
            return Task.FromResult<string?>(plain);
        }

        return Task.FromResult<string?>(null);
    }

    public Task RemoveAsync(string userId, string provider, CancellationToken cancellationToken)
    {
        var key = ComposeKey(userId, provider);
        _secrets.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private string Encrypt(string userId, string provider, string plainText)
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[SaltSize];
        rng.GetBytes(salt);
        var key = DeriveKey(userId, provider, salt);
        var nonce = new byte[NonceSize];
        rng.GetBytes(nonce);
        using var aes = new AesGcm(key);
        var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        var payload = new byte[salt.Length + nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, payload, 0, salt.Length);
        Buffer.BlockCopy(nonce, 0, payload, salt.Length, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, salt.Length + nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, salt.Length + nonce.Length + tag.Length, ciphertext.Length);
        return Convert.ToBase64String(payload);
    }

    private string Decrypt(string userId, string provider, string encrypted)
    {
        var payload = Convert.FromBase64String(encrypted);
        var salt = payload[..SaltSize];
        var nonce = payload[SaltSize..(SaltSize + NonceSize)];
        var tag = payload[(SaltSize + NonceSize)..(SaltSize + NonceSize + AesGcm.TagByteSizes.MaxSize)];
        var ciphertext = payload[(SaltSize + NonceSize + AesGcm.TagByteSizes.MaxSize)..];
        var key = DeriveKey(userId, provider, salt);
        using var aes = new AesGcm(key);
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] DeriveKey(string userId, string provider, byte[] salt)
    {
        var password = string.Concat(_options.MasterKey, ":", userId, ":", provider);
        using var derive = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return derive.GetBytes(KeySizeBits / 8);
    }

    private static string ComposeKey(string userId, string provider) => $"{userId}:{provider}";
}
