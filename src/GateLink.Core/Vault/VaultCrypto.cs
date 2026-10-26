using System.Security.Cryptography;
using System.Text;

namespace GateLink.Core.Vault;

/// <summary>
/// AES-256-GCM envelope for the on-disk vault. Format:
///   "GLV1" | 12-byte nonce | 16-byte tag | ciphertext
/// The key is 32 random bytes held by the platform key store, never in the file.
/// </summary>
public static class VaultCrypto
{
    public const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Magic = "GLV1"u8.ToArray();

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[Magic.Length + NonceSize + TagSize + ciphertext.Length];
        Magic.CopyTo(output, 0);
        nonce.CopyTo(output, Magic.Length);
        tag.CopyTo(output, Magic.Length + NonceSize);
        ciphertext.CopyTo(output, Magic.Length + NonceSize + TagSize);
        return output;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        var header = Magic.Length + NonceSize + TagSize;
        if (envelope.Length < header)
            throw new CryptographicException("Vault file is truncated.");

        if (!envelope[..Magic.Length].SequenceEqual(Magic))
            throw new CryptographicException("Vault file has an unknown format.");

        var nonce = envelope.Slice(Magic.Length, NonceSize);
        var tag = envelope.Slice(Magic.Length + NonceSize, TagSize);
        var ciphertext = envelope[(header)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public static string EncodeKey(ReadOnlySpan<byte> key) => Convert.ToBase64String(key);

    public static byte[] DecodeKey(string encoded)
    {
        var key = Convert.FromBase64String(encoded.Trim());
        if (key.Length != KeySize)
            throw new CryptographicException("Vault key has the wrong length.");
        return key;
    }

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
            throw new CryptographicException($"Vault key must be {KeySize} bytes.");
    }
}

/// <summary>
/// Merge rules for metadata-only saves: a connect/add flow must never erase the
/// stored seed or VPN password just because the in-memory entry came from the
/// plaintext metadata index.
/// </summary>
public static class VaultMerge
{
    public static VaultEntry Merge(VaultEntry? existing, VaultEntry updated)
    {
        if (existing is null)
        {
            if (string.IsNullOrEmpty(updated.Secret))
                throw new InvalidOperationException("A new vault entry needs a TOTP secret.");
            return updated;
        }

        return updated with
        {
            Secret = string.IsNullOrEmpty(updated.Secret) ? existing.Secret : updated.Secret,
            VpnPassword = string.IsNullOrEmpty(updated.VpnPassword) ? existing.VpnPassword : updated.VpnPassword,
            VpnUsername = string.IsNullOrEmpty(updated.VpnUsername) ? existing.VpnUsername : updated.VpnUsername,
        };
    }
}
