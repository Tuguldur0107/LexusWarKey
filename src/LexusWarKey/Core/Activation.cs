using System.Security.Cryptography;
using System.Text;

namespace LexusWarKey.Core;

public sealed record ActivationResult(
    bool Valid, string? UserId, DateTimeOffset? ExpiresUtc, string? Error, bool IsLegacy = false);

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
        Validate(token, nowUtc, Convert.FromBase64String(PublicKeyBase64), MachineId.Current);

    /// <summary>Overload taking the key and machine explicitly so tests can sign with their
    /// own pair and pretend to be any computer.</summary>
    public static ActivationResult Validate(
        string? token, DateTimeOffset nowUtc, byte[] publicKeySpki, string thisMachine)
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

        // Two shapes: "userId|expiry" is the original, machine-agnostic form, kept working so
        // nobody is locked out mid-migration. "userId|machineId|expiry" is bound to one
        // computer — a code passed to a friend simply does not validate on their machine.
        var pieces = Encoding.UTF8.GetString(payload).Split('|');
        string? boundMachine = null;
        long expiryUnix;
        switch (pieces.Length)
        {
            case 2 when long.TryParse(pieces[1], out expiryUnix):
                break;
            case 3 when long.TryParse(pieces[2], out expiryUnix):
                boundMachine = pieces[1];
                break;
            default:
                return new ActivationResult(false, null, null, "Кодын агуулга танигдсангүй.");
        }

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
            return new ActivationResult(false, userId, expires, $"Ашиглах хугацаа дууссан байна — Lexus Discord server дээр \"/warkey {thisMachine}\" гэж бичээд хугацаагаа сунгаарай.");

        // Signature and expiry are fine, so this is a genuine code — it just belongs to a
        // different computer. Say so precisely: the usual cause is a shared code, but it is
        // also what a reinstall or a new PC looks like, and that has an easy answer.
        if (boundMachine is not null
            && !boundMachine.Equals(thisMachine, StringComparison.OrdinalIgnoreCase))
        {
            return new ActivationResult(false, userId, expires,
                $"Энэ код өөр компьютерийнх байна — Lexus Discord server дээр \"/warkey {thisMachine}\" " +
                "гэж бичээд энэ компьютерийн кодоо аваарай.");
        }

        return new ActivationResult(true, userId, expires, null, IsLegacy: boundMachine is null);
    }

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
