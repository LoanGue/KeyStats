using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using KeyStats.Core.Control;

namespace KeyStats.Core.Tests;

public sealed class ControlServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static string NewPipe() => "KeyStats.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Chaque_commande_arrive_et_sa_reponse_revient()
    {
        string name = NewPipe();
        var seen = new ConcurrentQueue<AgentCommand>();
        using var server = new ControlServer(name, c => { seen.Enqueue(c); return c == AgentCommand.Quit ? AgentReply.Failed : AgentReply.Ok; });
        server.Start();
        foreach (var command in Enum.GetValues<AgentCommand>())
            Assert.Equal(command == AgentCommand.Quit ? AgentReply.Failed : AgentReply.Ok, await ControlClient.SendAsync(name, command, Timeout));
        Assert.Equal(Enum.GetValues<AgentCommand>(), seen.ToArray());
    }

    [Fact]
    public async Task Exception_du_gestionnaire_donne_un_echec()
    {
        string name = NewPipe();
        using var server = new ControlServer(name, _ => throw new InvalidOperationException("secret"));
        server.Start();
        Assert.Equal(AgentReply.Failed, await ControlClient.SendAsync(name, AgentCommand.Quit, Timeout));
        // Le serveur sert encore le client suivant.
        Assert.Equal(AgentReply.Failed, await ControlClient.SendAsync(name, AgentCommand.Quit, Timeout));
    }

    [Theory]
    [InlineData("bonjour\n", "unknown")]
    [InlineData("flushflushflushflushflushflushflush\n", "")]
    public async Task Ligne_illisible_sans_appel_au_gestionnaire(string line, string expected)
    {
        string name = NewPipe();
        int calls = 0;
        using var server = new ControlServer(name, _ => { Interlocked.Increment(ref calls); return AgentReply.Ok; });
        server.Start();
        Assert.Equal(expected, await Raw(name, Encoding.ASCII.GetBytes(line)));
        Assert.Equal(0, calls);
        // Le serveur sert encore le client suivant.
        Assert.Equal(AgentReply.Ok, await ControlClient.SendAsync(name, AgentCommand.Flush, Timeout));
    }

    [Fact]
    public async Task Client_muet_ne_bloque_pas_les_suivants()
    {
        string name = NewPipe();
        using var server = new ControlServer(name, _ => AgentReply.Ok, TimeSpan.FromMilliseconds(300));
        server.Start();
        using var mute = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await mute.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AgentReply.Ok, await ControlClient.SendAsync(name, AgentCommand.Flush, Timeout));
    }

    [Fact]
    public async Task Client_parti_avant_le_demarrage_ne_casse_pas_le_serveur()
    {
        string name = NewPipe();
        using var server = new ControlServer(name, _ => AgentReply.Ok);
        using (var gone = new NamedPipeClientStream(".", name, PipeDirection.InOut))
            gone.Connect(1000);
        server.Start();
        Assert.Equal(AgentReply.Ok, await ControlClient.SendAsync(name, AgentCommand.Flush, Timeout));
        Assert.True(server.Running);
    }

    [Fact]
    public async Task Gestionnaire_plus_lent_que_le_delai_client_repond_quand_meme()
    {
        string name = NewPipe();
        using var server = new ControlServer(name, _ => { Thread.Sleep(600); return AgentReply.Ok; }, TimeSpan.FromMilliseconds(300));
        server.Start();
        Assert.Equal(AgentReply.Ok, await ControlClient.SendAsync(name, AgentCommand.Flush, Timeout));
    }

    [Fact]
    public async Task Arret_pendant_un_gestionnaire()
    {
        string name = NewPipe();
        using var entered = new ManualResetEventSlim();
        var server = new ControlServer(name, _ => { entered.Set(); Thread.Sleep(300); return AgentReply.Ok; });
        server.Start();
        var send = ControlClient.SendAsync(name, AgentCommand.Quit, Timeout);
        Assert.True(entered.Wait(Timeout, TestContext.Current.CancellationToken));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        server.Dispose();
        server.Dispose();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(AgentReply.Ok, await send);
        Assert.False(server.Running);
    }

    [Fact]
    public async Task Sans_agent_pas_de_reponse()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await ControlClient.SendAsync(NewPipe(), AgentCommand.Flush, TimeSpan.FromMilliseconds(300)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Second_serveur_du_meme_nom_refuse()
    {
        string name = NewPipe();
        using var first = new ControlServer(name, _ => AgentReply.Ok);
        Assert.ThrowsAny<IOException>(() => new ControlServer(name, _ => AgentReply.Ok));
    }

    [Fact]
    public async Task Arret_rapide_en_attente_de_client()
    {
        string name = NewPipe();
        var server = new ControlServer(name, _ => AgentReply.Ok);
        server.Start();
        Assert.True(server.Stop(TimeSpan.FromSeconds(2)));
        server.Dispose();
        Assert.Null(await ControlClient.SendAsync(name, AgentCommand.Flush, TimeSpan.FromMilliseconds(300)));
    }

    /// <summary>Envoie des octets bruts et rend la ligne de réponse, vide si le serveur a coupé.</summary>
    private static async Task<string> Raw(string name, byte[] bytes)
    {
        var token = TestContext.Current.CancellationToken;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(token);
        try
        {
            await pipe.WriteAsync(bytes, token);
            using var reader = new StreamReader(pipe, Encoding.ASCII);
            return await reader.ReadLineAsync(token) ?? "";
        }
        catch (IOException)
        {
            return "";
        }
    }
}
