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

    private const string ThisMachine = "A1B2C3D4";
    private const string OtherMachine = "99887766";

    /// <summary>A machine-bound token, the shape the bot issues now.</summary>
    private static string BoundToken(string userId, string machine, DateTimeOffset expires)
    {
        var payload = Encoding.UTF8.GetBytes($"{userId}|{machine}|{expires.ToUnixTimeSeconds()}");
        var signature = Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

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
        var result = Activation.Validate(Token("298000001", Now.AddDays(30)), Now, PublicKey, ThisMachine);

        Assert.True(result.Valid);
        Assert.Equal("298000001", result.UserId);
        Assert.Equal(Now.AddDays(30).ToUnixTimeSeconds(), result.ExpiresUtc!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void Surrounding_whitespace_from_a_sloppy_copy_paste_is_forgiven()
    {
        var result = Activation.Validate("  " + Token("1", Now.AddDays(1)) + "\r\n", Now, PublicKey, ThisMachine);

        Assert.True(result.Valid);
    }

    [Fact]
    public void An_expired_token_is_rejected_and_says_how_to_get_a_new_one()
    {
        var result = Activation.Validate(Token("1", Now.AddDays(-1)), Now, PublicKey, ThisMachine);

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

        Assert.False(Activation.Validate(forged, Now, PublicKey, ThisMachine).Valid);
    }

    [Fact]
    public void A_token_signed_with_someone_elses_key_is_rejected()
    {
        using var impostor = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var result = Activation.Validate(Token("1", Now.AddDays(30), impostor), Now, PublicKey, ThisMachine);

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
        var result = Activation.Validate(token, Now, PublicKey, ThisMachine);

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

    // ---- machine binding: a code handed to a friend must not work on their PC ----

    [Fact]
    public void A_code_issued_for_this_machine_is_accepted()
    {
        var result = Activation.Validate(
            BoundToken("298000001", ThisMachine, Now.AddDays(30)), Now, PublicKey, ThisMachine);

        Assert.True(result.Valid);
        Assert.Equal("298000001", result.UserId);
        Assert.False(result.IsLegacy);
    }

    [Fact]
    public void The_same_code_on_someone_elses_machine_is_rejected()
    {
        var shared = BoundToken("298000001", OtherMachine, Now.AddDays(30));

        var result = Activation.Validate(shared, Now, PublicKey, ThisMachine);

        Assert.False(result.Valid);
        Assert.Contains("өөр компьютерийнх", result.Error);
        Assert.Contains(ThisMachine, result.Error); // tells them what to ask the bot for
    }

    [Fact]
    public void Machine_matching_ignores_case()
    {
        var result = Activation.Validate(
            BoundToken("1", ThisMachine.ToLowerInvariant(), Now.AddDays(1)), Now, PublicKey, ThisMachine);

        Assert.True(result.Valid);
    }

    [Fact]
    public void A_bound_code_cannot_be_rewritten_for_another_machine()
    {
        // Swap the machine field and keep the signature: the payload no longer verifies.
        var honest = BoundToken("1", OtherMachine, Now.AddDays(1));
        var forgedPayload = Base64Url(Encoding.UTF8.GetBytes($"1|{ThisMachine}|{Now.AddDays(1).ToUnixTimeSeconds()}"));

        var result = Activation.Validate(forgedPayload + "." + honest.Split('.')[1], Now, PublicKey, ThisMachine);

        Assert.False(result.Valid);
    }

    [Fact]
    public void Old_unbound_codes_still_work_but_are_flagged_as_legacy()
    {
        // Nobody gets locked out mid-migration; the UI nudges them to refresh.
        var result = Activation.Validate(Token("1", Now.AddDays(10)), Now, PublicKey, ThisMachine);

        Assert.True(result.Valid);
        Assert.True(result.IsLegacy);
    }

    [Fact]
    public void An_expired_bound_code_reports_expiry_not_the_wrong_machine()
    {
        var result = Activation.Validate(
            BoundToken("1", OtherMachine, Now.AddDays(-1)), Now, PublicKey, ThisMachine);

        Assert.False(result.Valid);
        Assert.Contains("хугацаа дууссан", result.Error);
    }

    [Fact]
    public void This_machines_id_is_stable_and_shaped_like_an_id()
    {
        var first = MachineId.Current;

        Assert.Equal(8, first.Length);
        Assert.Matches("^[0-9A-F]{8}$", first);
        Assert.Equal(first, MachineId.Current); // cached, never drifts within a session
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
