using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class RequestBodyTests
{
    /// <summary>Comme le flux COM de WebView2 : refuse tout décalage non nul, rend au plus <c>step</c> octets.</summary>
    private sealed class ComLikeStream(byte[] data, int step) : Stream
    {
        private int _position;

        public long Requested { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (offset != 0) throw new NotImplementedException();
            Requested += count;
            int n = Math.Min(Math.Min(count, step), data.Length - _position);
            data.AsSpan(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte[] Data(int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(i * 7))];

    [Fact]
    public void Sans_flux_corps_vide() => Assert.Empty(RequestBody.Read(null));

    [Fact]
    public void Lectures_courtes_sans_decalage_reassemblees()
    {
        byte[] data = Data(50);
        Assert.Equal(data, RequestBody.Read(new ComLikeStream(data, 1)));
        Assert.Equal(data, RequestBody.Read(new ComLikeStream(data, 4096)));
    }

    [Fact]
    public void Corps_de_taille_maximale_lu_en_entier()
    {
        byte[] data = Data(WebRouter.MaxBody);
        Assert.Equal(data, RequestBody.Read(new ComLikeStream(data, 3000)));
    }

    [Fact]
    public void Corps_trop_long_arrete_a_un_octet_de_plus()
    {
        var stream = new ComLikeStream(Data(20_000), 4096);
        Assert.Equal(WebRouter.MaxBody + 1, RequestBody.Read(stream).Length);
        Assert.Equal(WebRouter.MaxBody + 1, stream.Requested);
    }
}
