namespace KeyStats.Core.Web;

/// <summary>Lecture bornée du corps d'une requête POST, avant <see cref="WebRouter.HandleAsync"/>.</summary>
public static class RequestBody
{
    private const int ChunkSize = 4096;

    /// <summary>
    /// Lit au plus <see cref="WebRouter.MaxBody"/> + 1 octets : un corps plus long est refusé par le routeur
    /// sans avoir été chargé en entier. Chaque morceau est lu au début d'un tampon de travail, puis recopié :
    /// le flux COM de WebView2 lève <see cref="NotImplementedException"/> pour tout décalage non nul.
    /// Les tampons de travail sont effacés, même si la lecture échoue.
    /// </summary>
    public static byte[] Read(Stream? content)
    {
        if (content is null) return [];
        byte[] buffer = new byte[WebRouter.MaxBody + 1];
        byte[] chunk = new byte[ChunkSize];
        try
        {
            int total = 0;
            while (total < buffer.Length
                && content.Read(chunk, 0, Math.Min(chunk.Length, buffer.Length - total)) is var read and > 0)
            {
                chunk.AsSpan(0, read).CopyTo(buffer.AsSpan(total));
                total += read;
            }
            return buffer[..total];
        }
        finally
        {
            Array.Clear(buffer);
            Array.Clear(chunk);
        }
    }
}
