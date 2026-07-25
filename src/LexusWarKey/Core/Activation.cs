using System.Security.Cryptography;
using System.Text;

namespace LexusWarKey.Core;

public sealed record ActivationResult(bool Valid, string? UserId, DateTimeOffset? ExpiresUtc, string? Error);

/// <summary>Checks the activation code the Lexus Discord server's TierBot hands out.
///
/// The app is for that community: TierBot's /warkey command gives any member with a tier a
/// personal code — their Discord id and an expiry, signed with the bot's private key. The app
/// verifies the signature OFFLINE against the public key below, so there is no login, no
/// server call, and nothing to be down. Membership is enforced by time: codes expire, and
/// only the bot — which checks the tier list — can mint new ones.</summary>
public static class Activation
{
    /// <summary>ECDSA P-256 public key (SPKI). The matching private key lives only with
    /// TierBot — never in this repository.</summary>
    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEX0Ld+8THIsGh1N+gzZis1RXv0bfCvVikdWH8yy64ZFyeuc82p+ybQO/C/g1Qn++R3aLb1zuKuOTnPD821SH1JA==";

    public static ActivationResult Validate(string? token, DateTimeOffset nowUtc) =>
        Validate(token, nowUtc, Convert.FromBase64String(PublicKeyBase64));

    /// <summary>Overload taking the key explicitly so tests can sign with their own pair.</summary>
    public static ActivationResult Validate(string? token, DateTimeOffset nowUtc, byte[] publicKeySpki)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new ActivationResult(false, null, null, "Код хоосон байна.");

        var parts = token.Trim().Split('.');
        if (parts.Length != 2)
            return new ActivationResult(false, null, null, "Кодын бүтэц буруу байна — Discord-оос ирснийг бүтнээр нь хуулаарай.");

        byte[] payload, signature;
        try
        {
            payload = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return new ActivationResult(false, null, null, "Кодыг уншиж чадсангүй — дутуу эсвэл өөрчлөгдсөн байна.");
        }

        var pieces = Encoding.UTF8.GetString(payload).Split('|');
        if (pieces.Length != 2 || !long.TryParse(pieces[1], out var expiryUnix))
            return new ActivationResult(false, null, null, "Кодын агуулга танигдсангүй.");

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
        }
        catch (CryptographicException)
        {
            return new ActivationResult(false, null, null, "Түлхүүр ачаалагдсангүй.");
        }

        // TierBot (Python `cryptography`) signs in DER; verify the same format.
        if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            return new ActivationResult(false, null, null, "Код хүчингүй байна — Lexus серверийн ботоос авсан код мөн үү?");

        var userId = pieces[0];
        var expires = DateTimeOffset.FromUnixTimeSeconds(expiryUnix);
        if (expires <= nowUtc)
            return new ActivationResult(false, userId, expires, "Кодын хугацаа дууссан — Discord дээр /warkey гэж бичээд шинийг аваарай.");

        return new ActivationResult(true, userId, expires, null);
    }

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
