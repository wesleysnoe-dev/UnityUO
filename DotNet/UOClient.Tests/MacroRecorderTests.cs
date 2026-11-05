using UOClient.Core;

namespace UOClient.Tests;

/// <summary>The macro recorder behind the hotkeys' record/playmacro actions.</summary>
public sealed class MacroRecorderTests
{
    [Fact]
    public void NotesOnlyWhileRecording()
    {
        var r = new MacroRecorder();
        r.Note("say before");
        r.Start();
        r.Note("cast 29");
        r.Note("target 0x00000042");
        r.Note("  ");
        List<string> recorded = r.Stop();
        r.Note("say after");

        Assert.False(r.Recording);
        Assert.Equal(new[] { "cast 29", "target 0x00000042" }, recorded);
    }

    [Fact]
    public void StartDiscardsTheOldRecordingAndCapsTheLength()
    {
        var r = new MacroRecorder();
        r.Start();
        r.Note("say old");
        r.Stop();
        r.Start();
        for (int i = 0; i < MacroRecorder.MaxActions + 5; i++)
            r.Note($"skill {i}");
        List<string> recorded = r.Stop();

        Assert.Equal(MacroRecorder.MaxActions, recorded.Count);
        Assert.Equal("skill 0", recorded[0]);
    }

    [Fact]
    public void StoppedRecordingIsACopy()
    {
        var r = new MacroRecorder();
        r.Start();
        r.Note("lastobject");
        List<string> recorded = r.Stop();
        r.Start();
        Assert.Single(recorded);
    }

    [Theory]
    [InlineData("target 0x40001234", true)]
    [InlineData("targetself", true)]
    [InlineData(" LastTarget ", true)]
    [InlineData("cast 29", false)]
    [InlineData("say target me", false)]
    [InlineData("useobject 0x40001234", false)]
    public void TargetActionsWaitForACursor(string action, bool waits) =>
        Assert.Equal(waits, MacroRecorder.IsTargetAction(action));

    [Fact]
    public void SerialsAreWrittenAsEightHexDigits() =>
        Assert.Equal("0x4000ABCD", MacroRecorder.Hex(0x4000ABCD));
}
