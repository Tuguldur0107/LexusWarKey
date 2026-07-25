using System.Security.Cryptography;
using System.Text;
using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The activation gate ties the app to the Lexus Discord community: TierBot signs
/// "userId|expiry" with its private key, the app verifies offline with the public one. A
/// mistake here either locks out the whole community or lets everyone in — both silent.</summary>
public class ActivationTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly byte[] PublicKey = Key.ExportSubjectPublicKeyInfo();

    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a token exactly the way the bot's /warkey command does.</summary>
    private static string Token(string userId, DateTimeOffset expires, ECDsa? signer = null)
    {
        var payload = Encoding.UTF8.GetBytes($"{userId}|{expires.ToUnixTimeSeconds()}");
        var signature = (signer ?? Key).SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void A_token_from_the_bot_is_accepted()
    {
        var result = Activation.Validate(Token("298000001", Now.AddDays(30)), Now, PublicKey);

        Assert.True(result.Valid);
        Assert.Equal("298000001", result.UserId);
        Assert.Equal(Now.AddDays(30).ToUnixTimeSeconds(), result.ExpiresUtc!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void Surrounding_whitespace_from_a_sloppy_copy_paste_is_forgiven()
    {
        var result = Activation.Validate("  " + Token("1", Now.AddDays(1)) + "\r\n", Now, PublicKey);

        Assert.True(result.Valid);
    }

    [Fact]
    public void An_expired_token_is_rejected_and_says_how_to_get_a_new_one()
    {
        var result = Activation.Validate(Token("1", Now.AddDays(-1)), Now, PublicKey);

        Assert.False(result.Valid);
        Assert.Contains("/warkey", result.Error);
    }

    [Fact]
    public void Editing_the_expiry_breaks_the_signature()
    {
        // Take a valid token and graft a fresh payload with a later expiry onto its signature.
        var honest = Token("1", Now.AddDays(1));
        var forgedPayload = Base64Url(Encoding.UTF8.GetBytes($"1|{Now.AddYears(10).ToUnixTimeSeconds()}"));
        var forged = forgedPayload + "." + honest.Split('.')[1];

        Assert.False(Activation.Validate(forged, Now, PublicKey).Valid);
    }

    [Fact]
    public void A_token_signed_with_someone_elses_key_is_rejected()
    {
        using var impostor = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var result = Activation.Validate(Token("1", Now.AddDays(30), impostor), Now, PublicKey);

        Assert.False(result.Valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    [InlineData("one.two.three")]
    [InlineData("!!!.###")]
    public void Garbage_fails_with_an_explanation_rather_than_throwing(string? token)
    {
        var result = Activation.Validate(token, Now, PublicKey);

        Assert.False(result.Valid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void The_embedded_production_key_loads()
    {
        // Validate() against the real key must fail on signature, not on key parsing.
        var result = Activation.Validate(Token("1", Now.AddDays(1)), Now);

        Assert.False(result.Valid);
        Assert.DoesNotContain("Түлхүүр", result.Error);
    }

    [Fact]
    public void An_unactivated_engine_remaps_nothing()
    {
        var profile = WarKeyProfile.CreateDefault();
        var engine = new RemapEngine(() => profile, () => true, activated: () => false);

        Assert.Equal(RemapAction.PassThrough,
            engine.Decide(VirtualKeys.Space, true, false, false).Action);
        Assert.Equal(RemapAction.PassThrough,
            engine.Decide(profile.ChatMacros[0].HotkeyVk, true, false, false).Action);
    }

    [Fact]
    public void Activation_turns_the_same_engine_back_on()
    {
        var profile = WarKeyProfile.CreateDefault();
        var activated = false;
        var engine = new RemapEngine(() => profile, () => true, () => activated);

        Assert.Equal(RemapAction.PassThrough, engine.Decide(VirtualKeys.Space, true, false, false).Action);

        activated = true;

        Assert.Equal(RemapAction.SendKey, engine.Decide(VirtualKeys.Space, true, false, false).Action);
    }
}
