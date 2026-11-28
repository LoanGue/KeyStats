using KeyStats.Core.Input;
using KeyStats.Core.Stats;

namespace KeyStats.Core.Tests;

public sealed class DetailPressesTests
{
    private const long T0 = Typist.Start;
    private const ushort E = 0x12, X = 0x2D, R = 0x13, M = 0x27, Kb = 1;

    private static InputEvent Down(long at, ushort key, ushort device = Kb) => new(T0 + at, EventKind.KeyDown, key, device);

    private static InputEvent Up(long at, ushort key, ushort device = Kb) => new(T0 + at, EventKind.KeyUp, key, device);

    private static List<DetailPress> Extract(IEnumerable<InputEvent> events, long endAt = 10_000, int max = 100) =>
        DetailPresses.Extract(events, Kb, T0, T0 + endAt, max).Presses;

    [Fact]
    public void HoldAndOverlapComeFromTheReleases()
    {
        // e tenue 0–52, x enfoncée à 45 par-dessus : 7 ms de chevauchement ; e tenue 123–173, sans chevauchement.
        var presses = Extract([Down(0, E), Down(45, X), Up(52, E), Up(103, X), Down(123, E), Up(173, E)]);

        Assert.Equal(
            [new DetailPress(E, 0, 52, null, false), new DetailPress(X, 45, 58, 7, false), new DetailPress(E, 123, 50, null, false)],
            presses);
    }

    [Fact]
    public void OverlapEndsAtTheFirstOfTheTwoReleases()
    {
        // x enfoncée à 10 et relâchée à 30, pendant que e reste tenue jusqu'à 100 : 20 ms.
        var presses = Extract([Down(0, E), Down(10, X), Up(30, X), Up(100, E)]);

        Assert.Equal(20, presses[1].OverlapMs);
    }

    [Fact]
    public void ModifiersLocksAndOtherDevicesAreLeftOut()
    {
        var presses = Extract([
            Down(0, KeyCode.LeftShift), Down(10, E), Up(40, E), Up(50, KeyCode.LeftShift),
            Down(60, KeyCode.CapsLock), Up(70, KeyCode.CapsLock), Down(80, X, device: 2), Up(90, X, device: 2),
        ]);

        Assert.Equal([new DetailPress(E, 10, 30, null, false)], presses);
    }

    [Fact]
    public void AutoRepeatStaysOneLongPress()
    {
        var presses = Extract([Down(0, E), Down(500, E), Down(530, E), Up(560, E)]);

        Assert.Equal([new DetailPress(E, 0, 560, null, false)], presses);
    }

    [Fact]
    public void BackspaceErasesTheLastCharacterKeys()
    {
        // e x e m p l r ⌫ e : le r est effacé, le retour arrière lui-même non.
        ushort[] keys = [E, X, E, M, 0x19, 0x26, R, KeyCode.Backspace, E];
        var events = keys.SelectMany((k, i) => new[] { Down(i * 100, k), Up(i * 100 + 50, k) });

        var presses = Extract(events);

        Assert.Equal([false, false, false, false, false, false, true, false, false], presses.Select(p => p.Erased));
    }

    [Fact]
    public void RepeatedBackspaceErasesOneKeyPerRepeat()
    {
        var presses = Extract([
            Down(0, E), Up(50, E), Down(100, X), Up(150, X), Down(200, R), Up(250, R),
            Down(300, KeyCode.Backspace), Down(800, KeyCode.Backspace), Up(820, KeyCode.Backspace),
        ]);

        Assert.Equal([false, true, true, false], presses.Select(p => p.Erased));
    }

    [Fact]
    public void CtrlBackspaceErasesTheLastWord()
    {
        var presses = Extract([
            Down(0, E), Up(50, E), Down(100, KeyCode.Space), Up(150, KeyCode.Space), Down(200, X), Up(250, X),
            Down(300, R), Up(350, R), Down(400, KeyCode.Space), Up(450, KeyCode.Space),
            Down(500, KeyCode.LeftCtrl), Down(520, KeyCode.Backspace), Up(560, KeyCode.Backspace), Up(580, KeyCode.LeftCtrl),
        ]);

        Assert.Equal([false, false, true, true, true, false], presses.Select(p => p.Erased));
    }

    [Theory]
    [InlineData(KeyCode.Left)]
    [InlineData(KeyCode.Delete)]
    [InlineData(KeyCode.Tab)]
    public void CursorMovesStopTheErasing(ushort move)
    {
        var presses = Extract([Down(0, E), Up(50, E), Down(100, move), Up(150, move), Down(200, KeyCode.Backspace), Up(250, KeyCode.Backspace)]);

        Assert.DoesNotContain(presses, p => p.Erased);
    }

    [Fact]
    public void AClickStopsTheErasingWhateverItsDevice()
    {
        var presses = Extract([
            Down(0, E), Up(50, E), new InputEvent(T0 + 100, EventKind.MouseDown, MouseButton.Left, 7),
            Down(200, KeyCode.Backspace), Up(250, KeyCode.Backspace),
        ]);

        Assert.False(presses[0].Erased);
    }

    [Fact]
    public void AltGrCharactersAreErasable()
    {
        // AltGr : faux Ctrl gauche puis Alt droit, comme Windows les envoie.
        var presses = Extract([
            Down(0, KeyCode.LeftCtrl), Down(0, KeyCode.RightAlt), Down(10, 0x03), Up(40, 0x03),
            Up(50, KeyCode.RightAlt), Up(50, KeyCode.LeftCtrl), Down(100, KeyCode.Backspace), Up(150, KeyCode.Backspace),
        ]);

        Assert.True(presses[0].Erased);
    }

    [Fact]
    public void BackspaceStillErasesKeysTypedBeforeTheSessionWithoutCountingThem()
    {
        // e tapée avant la session : le retour arrière l'efface sans toucher au x de la session.
        var presses = DetailPresses.Extract(
            [Down(-500, E), Up(-450, E), Down(0, X), Up(50, X), Down(100, KeyCode.Backspace), Down(600, KeyCode.Backspace)],
            Kb, T0, T0 + 1000, 100).Presses;

        Assert.Equal([true, false], presses.Select(p => p.Erased));
    }

    [Fact]
    public void AKeyHeldFromBeforeTheSessionIsNotAPress()
    {
        var presses = Extract([Down(-100, E), Down(400, E), Up(450, E), Down(500, X), Up(550, X)]);

        Assert.Equal([X], presses.Select(p => (ushort)p.Key));
    }

    [Fact]
    public void AResetLosesTheReleasesOfHeldKeys()
    {
        var presses = Extract([Down(0, E), new InputEvent(T0 + 30, EventKind.Reset, 0, Kb), Up(50, E)]);

        Assert.Null(presses[0].HoldMs);
    }

    [Fact]
    public void ReleasesAfterTheEndAreReadButNotNewPresses()
    {
        var presses = Extract([Down(0, E), Down(900, X), Up(1100, X), Up(1200, E), Down(1300, R)], endAt: 1000);

        Assert.Equal([new DetailPress(E, 0, 1200, null, false), new DetailPress(X, 900, 200, 200, false)], presses);
    }

    [Fact]
    public void TheCapStopsTheListAndSaysSo()
    {
        var events = Enumerable.Range(0, 10).SelectMany(i => new[] { Down(i * 100, E), Up(i * 100 + 50, E) });

        var (presses, truncated) = DetailPresses.Extract(events, Kb, T0, T0 + 10_000, 4);

        Assert.Equal(4, presses.Count);
        Assert.True(truncated);
    }
}
