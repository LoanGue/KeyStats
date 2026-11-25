using KeyStats.Core.Input;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class RawCodecTests(ITestOutputHelper output)
{
    [Fact]
    public void Aller_retour_redonne_les_memes_evenements_a_la_milliseconde()
    {
        long t = Typist.Start;
        InputEvent[] events =
        [
            new(t, EventKind.Reset, 0, 0, (byte)(LockKeys.NumLock | LockKeys.CapsLock)),
            new(t + 5, EventKind.KeyDown, 0x10, 1),
            new(t + 5, EventKind.KeyDown, KeyCode.LeftShift, 1),
            new(t + 90, EventKind.KeyUp, 0x10, 1),
            new(t + 91, EventKind.KeyUp, KeyCode.LeftShift, 1),
            new(t + 200, EventKind.KeyDown, KeyCode.Left, 1),
            new(t + 260, EventKind.KeyUp, KeyCode.Left, 1),
            new(t + 300, EventKind.MouseDown, MouseButton.Left, 2),
            new(t + 380, EventKind.MouseUp, MouseButton.Left, 2),
            new(t + 400, EventKind.KeyDown, KeyCode.Pause, 3),
            new(t + 401, EventKind.KeyDown, KeyCode.VirtualKey | 0xAD, 3),
            new(t + 402, EventKind.ForcedUp, KeyCode.RightCtrl, 3),
            new(t + 70_000, EventKind.KeyDown, KeyCode.NumEnter, 1),
            new(t + 70_000, EventKind.KeyUp, KeyCode.NumEnter, 1, 1),
        ];

        byte[] payload = RawCodec.Encode(events, t);
        var decoded = RawCodec.Decode(payload, t);

        Assert.Equal(events, decoded);
    }

    [Fact]
    public void Bloc_vide()
    {
        Assert.Empty(RawCodec.Decode(RawCodec.Encode([], 42), 42));
    }

    [Fact]
    public void Frappe_reelle_tient_en_quelques_octets_par_evenement()
    {
        var rng = new Random(12345);
        var t = new Typist();
        const string words = "le la les un une des et est dans pour que qui sur avec pas plus mais comme tout bien fait";
        string[] list = words.Split(' ');
        for (int i = 0; i < 2000; i++)
        {
            t.Step = 120 + rng.Next(200);
            t.HoldMs = 60 + rng.Next(60);
            t.Type(list[rng.Next(list.Length)]).TypeChar(' ');
            if (rng.Next(20) == 0) t.Backspace(1 + rng.Next(3));
        }
        var events = t.ToArray();

        byte[] payload = RawCodec.Encode(events, events[0].Ms);
        double perEvent = (double)payload.Length / events.Length;
        output.WriteLine($"{events.Length} événements, {payload.Length} octets, {perEvent:F2} octets par événement");

        Assert.Equal(events, RawCodec.Decode(payload, events[0].Ms));
        Assert.True(perEvent < 3, $"{perEvent:F2} octets par événement");
    }

    [Fact]
    public void Bloc_tronque_ou_corrompu_est_refuse()
    {
        var events = new Typist().Type("bonjour").ToArray();
        byte[] payload = RawCodec.Encode(events, events[0].Ms);

        Assert.ThrowsAny<InvalidDataException>(() => RawCodec.Decode(payload.AsSpan(0, payload.Length - 3), events[0].Ms));
        payload[0] = 99;
        Assert.ThrowsAny<InvalidDataException>(() => RawCodec.Decode(payload, events[0].Ms));
    }

    [Fact]
    public void Varint_aller_retour()
    {
        ulong[] values = [0, 1, 127, 128, 300, 16_383, 16_384, uint.MaxValue, ulong.MaxValue];
        Span<byte> buf = stackalloc byte[Varint.MaxLength];
        foreach (ulong v in values)
        {
            int len = Varint.Write(buf, v);
            int pos = 0;
            Assert.Equal(v, Varint.Read(buf[..len], ref pos));
            Assert.Equal(len, pos);
        }
    }
}
