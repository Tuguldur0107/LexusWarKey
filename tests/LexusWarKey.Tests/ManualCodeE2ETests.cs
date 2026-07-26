using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>A code minted by the offline generator, verified by the app's real embedded key.
/// The generator exists so access can be handed to someone with no Discord account at all,
/// so the only thing that matters is that the app treats it exactly like a bot-issued one.</summary>
public class ManualCodeE2ETests
{
    // Produced by warkey_code.py for machine A1B2C3D4, 7 days, label "Test guest".
    private const string Token =
        "bWFudWFsOlRlc3QgZ3Vlc3R8QTFCMkMzRDR8MTc4NTY4NzA4OQ.MEQCID6ZYQ61c-uk6KotZItX5-eNC8iEv78Uzn-Oq2Zrb8vwAiArAVZVsdCC8P_IqrNiN8rM3k_-RVfcZ1ioW7Wp74BNOA";

    private static byte[] ProductionKey => Convert.FromBase64String(Activation.PublicKeyBase64);

    [Fact]
    public void The_offline_generator_produces_a_code_the_app_accepts()
    {
        var result = Activation.Validate(Token, DateTimeOffset.UtcNow, ProductionKey, "A1B2C3D4");

        Assert.True(result.Valid);
        Assert.False(result.IsLegacy);              // machine-bound, like any other
        Assert.Equal("manual:Test guest", result.UserId);
    }

    [Fact]
    public void It_is_bound_to_its_machine_like_any_other_code()
    {
        var elsewhere = Activation.Validate(Token, DateTimeOffset.UtcNow, ProductionKey, "99887766");

        Assert.False(elsewhere.Valid);
        Assert.Contains("өөр компьютерийнх", elsewhere.Error);
    }

    [Fact]
    public void It_expires_on_the_day_it_was_issued_for()
    {
        var expiry = Activation.Validate(Token, DateTimeOffset.UtcNow, ProductionKey, "A1B2C3D4").ExpiresUtc!.Value;
        var afterwards = expiry.AddMinutes(1);

        Assert.False(Activation.Validate(Token, afterwards, ProductionKey, "A1B2C3D4").Valid);
    }
}
