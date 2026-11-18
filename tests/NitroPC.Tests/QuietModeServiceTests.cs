using NitroPC.Models;
using NitroPC.Services;

namespace NitroPC.Tests;

public sealed class QuietModeServiceTests
{
    [Fact]
    public void EnablePersistsOriginalPolicyAndDisablesBoost()
    {
        var original = OriginalState();
        var platform = new FakePlatform(original);
        QuietModeRecoveryState? persisted = null;
        using var service = new QuietModeService(platform, null, state => persisted = state);

        service.Enable();

        Assert.True(service.IsActive);
        Assert.Equal(original, persisted);
        Assert.Equal(QuietState(original), platform.AppliedStates.Single());
    }

    [Fact]
    public void DisableRestoresOriginalPolicyAndClearsRecoveryState()
    {
        var original = OriginalState();
        var platform = new FakePlatform(original);
        QuietModeRecoveryState? persisted = null;
        using var service = new QuietModeService(platform, null, state => persisted = state);
        service.Enable();

        service.Disable();

        Assert.False(service.IsActive);
        Assert.Null(persisted);
        Assert.Equal([QuietState(original), original], platform.AppliedStates);
    }

    [Fact]
    public void InterruptedSessionIsRestoredOnStartup()
    {
        var original = OriginalState();
        var platform = new FakePlatform(original);
        QuietModeRecoveryState? persisted = original;
        using var service = new QuietModeService(platform, original, state => persisted = state);

        service.RecoverInterruptedSession();

        Assert.Equal(original, platform.AppliedStates.Single());
        Assert.Null(persisted);
        Assert.False(service.IsActive);
    }

    [Fact]
    public void FailedEnableRollsBackAndClearsRecoveryState()
    {
        var original = OriginalState();
        var platform = new FakePlatform(original) { FailNextApply = true };
        QuietModeRecoveryState? persisted = null;
        using var service = new QuietModeService(platform, null, state => persisted = state);

        Assert.Throws<System.ComponentModel.Win32Exception>(() => service.Enable());

        Assert.False(service.IsActive);
        Assert.Null(persisted);
        Assert.Equal([QuietState(original), original], platform.AppliedStates);
    }

    private static QuietModeRecoveryState OriginalState() =>
        new(Guid.NewGuid(), 2, 4);

    private static QuietModeRecoveryState QuietState(QuietModeRecoveryState state) =>
        state with { AcProcessorBoostMode = 0, DcProcessorBoostMode = 0 };

    private sealed class FakePlatform : IQuietModePlatform
    {
        private readonly QuietModeRecoveryState _original;

        public FakePlatform(QuietModeRecoveryState original)
        {
            _original = original;
        }

        public List<QuietModeRecoveryState> AppliedStates { get; } = [];

        public bool FailNextApply { get; set; }

        public QuietModeRecoveryState CaptureActiveBoostState() => _original;

        public void ApplyBoostState(QuietModeRecoveryState state)
        {
            AppliedStates.Add(state);
            if (FailNextApply)
            {
                FailNextApply = false;
                throw new System.ComponentModel.Win32Exception(5, "Denied.");
            }
        }
    }
}
