// NOTE: VaultEncryption (Shroud) only exposes a string-based password API, so the password
// must briefly become a managed string at each Encrypt/Decrypt call site. To shrink the
// exposure window, _password is stored as a UTF-8 byte[] and converted to string inline at
// each use (via GetPassword). The string is never cached -- it lives only for the duration of
// one Encrypt/Decrypt call and is then eligible for GC. Contrast with the previous approach
// of storing a string field directly, which kept the password on the managed heap for the
// entire session lifetime. _password bytes are zeroed on Dispose.
//
// If a future Shroud version exposes a byte[]-based API, GetPassword can be eliminated and
// the bytes passed directly, removing the transient string entirely.

namespace LogMark.Models.Sensitivity;

/// <summary>
/// Opaque handle to an unlocked registry. Wraps the derived key material.
/// Implementations zero the key on Dispose.
///
/// Extended slots for the closed sensitivity provider:
///   - _maskingKey: the 32-byte HMAC key used by SpanMasker (stored inside vault.shroud,
///     retrieved during Unlock). Kept here so Mask() can create a SpanMasker without
///     re-reading the registry.
///   - _password: the vault password (stored as UTF-8 bytes), kept for AppendTokens /
///     RotatePassword / VaultFolder which must re-encrypt the vault. Zeroed on Dispose
///     alongside other byte arrays. Converted to string transiently at each call site
///     via GetPassword() -- see file-top comment for rationale.
///
/// The original _keyMaterial slot is kept for backward compatibility with the interface
/// contract (GetKeyMaterial); the closed sensitivity provider does not use it for its own purposes
/// (it uses GetMaskingKey and GetPassword instead) but callers that only depend on the
/// interface need not care about these extras.
/// </summary>
public sealed class SessionKey : IDisposable
{
    private byte[]? _keyMaterial;
    private byte[]? _maskingKey;
    private byte[]? _password;

    public SessionKey(byte[] keyMaterial)
    {
        _keyMaterial = keyMaterial ?? throw new ArgumentNullException(nameof(keyMaterial));
    }

    /// <summary>
    /// Factory used by the closed sensitivity provider. Carries the HMAC masking key and vault
    /// password (as UTF-8 bytes) in addition to the generic keyMaterial slot (set to the
    /// masking key bytes for the generic contract).
    /// </summary>
    internal static SessionKey ForShroud(byte[] maskingKey, byte[] passwordBytes)
    {
        if (maskingKey is null) throw new ArgumentNullException(nameof(maskingKey));
        if (passwordBytes is null) throw new ArgumentNullException(nameof(passwordBytes));
        return new SessionKey(maskingKey)
        {
            _maskingKey = (byte[])maskingKey.Clone(),
            _password = (byte[])passwordBytes.Clone()
        };
    }

    internal byte[] GetKeyMaterial()
    {
        if (_keyMaterial is null)
            throw new ObjectDisposedException(nameof(SessionKey));
        return _keyMaterial;
    }

    /// <summary>Returns the 32-byte HMAC masking key stored inside vault.shroud.</summary>
    internal byte[] GetMaskingKey()
    {
        if (_maskingKey is null)
            throw new InvalidOperationException(
                "SessionKey was not created via ForShroud - masking key slot is empty.");
        return _maskingKey;
    }

    /// <summary>
    /// Returns the vault password as a string for passing to VaultEncryption (string-only API).
    /// The string is constructed fresh on each call from the stored UTF-8 bytes and is not
    /// cached -- it lives only for the duration of the Encrypt/Decrypt call at the use site.
    /// </summary>
    internal string GetPassword()
    {
        if (_password is null)
            throw new InvalidOperationException(
                "SessionKey was not created via ForShroud - password slot is empty.");
        return System.Text.Encoding.UTF8.GetString(_password);
    }

    public bool IsDisposed => _keyMaterial is null;

    public void Dispose()
    {
        if (_keyMaterial is not null)
        {
            Array.Clear(_keyMaterial, 0, _keyMaterial.Length);
            _keyMaterial = null;
        }
        if (_maskingKey is not null)
        {
            Array.Clear(_maskingKey, 0, _maskingKey.Length);
            _maskingKey = null;
        }
        if (_password is not null)
        {
            Array.Clear(_password, 0, _password.Length);
            _password = null;
        }
    }
}
