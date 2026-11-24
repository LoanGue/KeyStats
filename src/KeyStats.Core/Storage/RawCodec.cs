using System.IO.Compression;
using KeyStats.Core.Input;

namespace KeyStats.Core.Storage;

/// <summary>
/// Format brut v1 : un bloc d'événements (60 s au plus) rangé en colonnes, puis compressé (Brotli).
/// Le chiffrement est fait à part (<see cref="Crypto.EpochCipher"/>).
/// <code>
/// charge   = version(1) · varint taille_en_clair · brotli(clair)
/// clair    = varint n
///            n × varint écart_ms           (avec l'événement précédent ; le premier avec start_ms)
///            n × octet code                (1–0x7F : touche ; 0x80|x : touche E0 0x100+x ; 0 : voir « spéciaux »)
///            ⌈m/8⌉ octets appui/relâchement (m = codes non nuls ; bit de poids faible d'abord ; 1 = relâchement)
///            spéciaux : nature(1) · varint touche · flags(1), dans l'ordre
///            varint r · r × (varint écart d'indice · varint appareil)   (l'appareil vaut 0 avant la première entrée)
/// </code>
/// Les horodatages doivent être croissants : un retour en arrière est ramené à un écart nul.
/// </summary>
public static class RawCodec
{
    public const byte Version = 1;
    private const int BrotliQuality = 11, BrotliWindow = 22;
    private const int MaxEvents = 1 << 22, MaxPlainBytes = 64 << 20;

    /// <summary>Encode et compresse un bloc.</summary>
    public static byte[] Encode(ReadOnlySpan<InputEvent> events, long startMs)
    {
        var plain = new ByteWriter(events.Length * 4 + 16);
        EncodePlain(events, startMs, plain);
        ReadOnlySpan<byte> src = plain.WrittenSpan;

        var output = new byte[1 + Varint.MaxLength + BrotliEncoder.GetMaxCompressedLength(src.Length)];
        output[0] = Version;
        int pos = 1 + Varint.Write(output.AsSpan(1), (ulong)src.Length);
        if (!BrotliEncoder.TryCompress(src, output.AsSpan(pos), out int written, BrotliQuality, BrotliWindow))
            throw new InvalidOperationException("Compression Brotli impossible.");
        return output.AsSpan(0, pos + written).ToArray();
    }

    /// <summary>Décompresse et décode un bloc.</summary>
    public static InputEvent[] Decode(ReadOnlySpan<byte> payload, long startMs)
    {
        if (payload.IsEmpty || payload[0] != Version) throw new InvalidDataException("Version du format brut inconnue.");
        int pos = 1;
        int plainLength = Varint.ReadInt(payload, ref pos);
        if (plainLength > MaxPlainBytes) throw new InvalidDataException("Bloc brut trop grand.");
        var plain = new byte[plainLength];
        if (!BrotliDecoder.TryDecompress(payload[pos..], plain, out int written) || written != plainLength)
            throw new InvalidDataException("Bloc brut corrompu.");
        return DecodePlain(plain, startMs);
    }

    internal static void EncodePlain(ReadOnlySpan<InputEvent> events, long startMs, ByteWriter w)
    {
        w.WriteVarint((ulong)events.Length);

        long prev = startMs;
        foreach (ref readonly var e in events)
        {
            long delta = e.Ms - prev;
            if (delta < 0) delta = 0;
            w.WriteVarint((ulong)delta);
            prev += delta;
        }

        foreach (ref readonly var e in events) w.WriteByte(SimpleCode(in e));

        byte bits = 0;
        int nBits = 0;
        foreach (ref readonly var e in events)
        {
            if (SimpleCode(in e) == 0) continue;
            if (e.Kind == EventKind.KeyUp) bits |= (byte)(1 << nBits);
            if (++nBits == 8)
            {
                w.WriteByte(bits);
                bits = 0;
                nBits = 0;
            }
        }
        if (nBits > 0) w.WriteByte(bits);

        foreach (ref readonly var e in events)
        {
            if (SimpleCode(in e) != 0) continue;
            w.WriteByte((byte)e.Kind);
            w.WriteVarint(e.Key);
            w.WriteByte(e.Flags);
        }

        int runs = 0;
        ushort device = 0;
        foreach (ref readonly var e in events)
        {
            if (e.Device != device)
            {
                runs++;
                device = e.Device;
            }
        }
        w.WriteVarint((ulong)runs);
        device = 0;
        int last = 0;
        for (int i = 0; i < events.Length; i++)
        {
            if (events[i].Device == device) continue;
            device = events[i].Device;
            w.WriteVarint((ulong)(i - last));
            w.WriteVarint(device);
            last = i;
        }
    }

    internal static InputEvent[] DecodePlain(ReadOnlySpan<byte> src, long startMs)
    {
        int pos = 0;
        int n = Varint.ReadInt(src, ref pos);
        if (n > MaxEvents) throw new InvalidDataException("Bloc brut trop grand.");
        var events = new InputEvent[n];

        long ms = startMs;
        for (int i = 0; i < n; i++)
        {
            ms += (long)Varint.Read(src, ref pos);
            events[i].Ms = ms;
        }

        if (pos + n > src.Length) throw new InvalidDataException("Bloc brut tronqué.");
        ReadOnlySpan<byte> codes = src.Slice(pos, n);
        pos += n;
        int simple = 0;
        foreach (byte c in codes) if (c != 0) simple++;

        int bitBytes = (simple + 7) / 8;
        if (pos + bitBytes > src.Length) throw new InvalidDataException("Bloc brut tronqué.");
        ReadOnlySpan<byte> bits = src.Slice(pos, bitBytes);
        pos += bitBytes;

        int bit = 0;
        for (int i = 0; i < n; i++)
        {
            byte c = codes[i];
            if (c != 0)
            {
                events[i].Key = c < 0x80 ? c : (ushort)(KeyCode.E0 | (c & 0x7F));
                events[i].Kind = (bits[bit >> 3] & (1 << (bit & 7))) != 0 ? EventKind.KeyUp : EventKind.KeyDown;
                bit++;
            }
            else
            {
                if (pos >= src.Length) throw new InvalidDataException("Bloc brut tronqué.");
                events[i].Kind = (EventKind)src[pos++];
                ulong key = Varint.Read(src, ref pos);
                if (key > ushort.MaxValue || pos >= src.Length) throw new InvalidDataException("Bloc brut corrompu.");
                events[i].Key = (ushort)key;
                events[i].Flags = src[pos++];
            }
        }

        int runs = Varint.ReadInt(src, ref pos);
        int start = 0;
        ushort device = 0;
        for (int r = 0; r < runs; r++)
        {
            int next = start + Varint.ReadInt(src, ref pos);
            ulong dev = Varint.Read(src, ref pos);
            if (next > n || next < start || dev > ushort.MaxValue) throw new InvalidDataException("Bloc brut corrompu.");
            for (int i = start; i < next; i++) events[i].Device = device;
            device = (ushort)dev;
            start = next;
        }
        for (int i = start; i < n; i++) events[i].Device = device;

        if (pos != src.Length) throw new InvalidDataException("Bloc brut : données en trop.");
        return events;
    }

    /// <summary>Code sur un octet des appuis et relâchements ordinaires ; 0 si l'événement est « spécial ».</summary>
    private static byte SimpleCode(in InputEvent e)
    {
        if (e.Flags != 0 || e.Kind is not (EventKind.KeyDown or EventKind.KeyUp)) return 0;
        if (e.Key is >= 1 and <= 0x7F) return (byte)e.Key;
        if (e.Key is >= KeyCode.E0 and <= KeyCode.E0 + 0x7F) return (byte)(0x80 | (e.Key & 0x7F));
        return 0;
    }
}
