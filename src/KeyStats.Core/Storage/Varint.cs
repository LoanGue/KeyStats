namespace KeyStats.Core.Storage;

/// <summary>Entiers à longueur variable (LEB128 non signé) : 7 bits par octet, bit fort = suite.</summary>
public static class Varint
{
    public const int MaxLength = 10;

    public static int Write(Span<byte> dest, ulong value)
    {
        int i = 0;
        while (value >= 0x80)
        {
            dest[i++] = (byte)(value | 0x80);
            value >>= 7;
        }
        dest[i++] = (byte)value;
        return i;
    }

    public static void Write(ByteWriter w, ulong value)
    {
        Span<byte> tmp = stackalloc byte[MaxLength];
        w.Write(tmp[..Write(tmp, value)]);
    }

    public static ulong Read(ReadOnlySpan<byte> src, ref int pos)
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            if (pos >= src.Length || shift > 63) throw new InvalidDataException("Entier variable tronqué.");
            byte b = src[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
    }

    public static int ReadInt(ReadOnlySpan<byte> src, ref int pos)
    {
        ulong v = Read(src, ref pos);
        if (v > int.MaxValue) throw new InvalidDataException("Entier hors limites.");
        return (int)v;
    }
}

/// <summary>Tampon d'écriture extensible, réutilisable (pas d'allocation une fois dimensionné).</summary>
public sealed class ByteWriter
{
    private byte[] _buf;
    private int _len;

    public ByteWriter(int capacity = 256) => _buf = new byte[Math.Max(16, capacity)];

    public int Length => _len;
    public ReadOnlySpan<byte> WrittenSpan => _buf.AsSpan(0, _len);

    public void Clear() => _len = 0;

    public void WriteByte(byte b)
    {
        if (_len == _buf.Length) Grow(1);
        _buf[_len++] = b;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_len + data.Length > _buf.Length) Grow(data.Length);
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;
    }

    public void WriteVarint(ulong value) => Varint.Write(this, value);

    public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();

    private void Grow(int extra)
    {
        int size = Math.Max(_buf.Length * 2, _len + extra);
        Array.Resize(ref _buf, size);
    }
}
