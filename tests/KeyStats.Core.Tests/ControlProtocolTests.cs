using System.Text;
using KeyStats.Core.Control;

namespace KeyStats.Core.Tests;

public sealed class ControlProtocolTests
{
    [Theory]
    [InlineData(AgentCommand.Snooze, "snooze")]
    [InlineData(AgentCommand.PauseUntilResumed, "pause")]
    [InlineData(AgentCommand.Resume, "resume")]
    [InlineData(AgentCommand.Flush, "flush")]
    [InlineData(AgentCommand.Quit, "quit")]
    [InlineData(AgentCommand.Reload, "reload")]
    public void Commande_encodee_sur_une_ligne(AgentCommand command, string word)
    {
        Assert.Equal(word + "\n", Encoding.ASCII.GetString(ControlProtocol.Encode(command)));
        Assert.True(ControlProtocol.TryParse(Encoding.ASCII.GetBytes(word), out AgentCommand parsed));
        Assert.Equal(command, parsed);
        Assert.True(ControlProtocol.TryParse(Encoding.ASCII.GetBytes(word + "\r"), out parsed));
        Assert.Equal(command, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Flush")]
    [InlineData("flush ")]
    [InlineData("flus")]
    [InlineData("flushx")]
    public void Ligne_inconnue_refusee(string line) =>
        Assert.False(ControlProtocol.TryParse(Encoding.ASCII.GetBytes(line), out AgentCommand _));

    [Theory]
    [InlineData(AgentReply.Ok, "ok")]
    [InlineData(AgentReply.Unknown, "unknown")]
    [InlineData(AgentReply.Failed, "failed")]
    public void Reponse_encodee_sur_une_ligne(AgentReply reply, string word)
    {
        Assert.Equal(word + "\n", Encoding.ASCII.GetString(ControlProtocol.Encode(reply)));
        Assert.True(ControlProtocol.TryParse(Encoding.ASCII.GetBytes(word), out AgentReply parsed));
        Assert.Equal(reply, parsed);
        Assert.True(ControlProtocol.TryParse(Encoding.ASCII.GetBytes(word + "\r"), out parsed));
        Assert.Equal(reply, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("OK")]
    [InlineData("ok ")]
    [InlineData("flush")]
    public void Reponse_inconnue_refusee(string line) =>
        Assert.False(ControlProtocol.TryParse(Encoding.ASCII.GetBytes(line), out AgentReply _));

    [Fact]
    public void Valeur_hors_protocole_refusee_a_l_encodage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlProtocol.Encode((AgentCommand)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlProtocol.Encode((AgentReply)9));
    }

    [Theory]
    [InlineData("flush\n", 5)]
    [InlineData("\n", 0)]
    [InlineData("flush\nreste", 5)]
    [InlineData("flush", -1)]
    [InlineData("", -1)]
    [InlineData("0123456789012345678901234567890\n", 31)]
    [InlineData("01234567890123456789012345678901", -1)]
    [InlineData("01234567890123456789012345678901\n", -1)]
    public async Task Lecture_d_une_ligne_bornee(string data, int expected)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(data);
        byte[] buffer = new byte[ControlProtocol.MaxLine];
        var token = TestContext.Current.CancellationToken;
        // D'un bloc, puis un octet par lecture : le saut de ligne peut arriver dans une lecture suivante.
        Assert.Equal(expected, await ControlProtocol.ReadLineAsync(new MemoryStream(bytes), buffer, token));
        Assert.Equal(expected, await ControlProtocol.ReadLineAsync(new Trickle(bytes), buffer, token));
        if (expected >= 0) Assert.Equal(data[..expected], Encoding.ASCII.GetString(buffer, 0, expected));
    }

    private sealed class Trickle(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    [Fact]
    public void Nom_du_tube_par_base_et_par_session()
    {
        string db = Path.Combine(Path.GetTempPath(), "KeyStats", "keystats.db");
        Assert.Equal(ControlProtocol.PipeName(1, db), ControlProtocol.PipeName(1, db.ToUpperInvariant()));
        Assert.NotEqual(ControlProtocol.PipeName(1, db), ControlProtocol.PipeName(2, db));
        Assert.NotEqual(ControlProtocol.PipeName(1, db), ControlProtocol.PipeName(1, db + "2"));
        Assert.Matches("^KeyStats\\.Agent\\.1\\.[0-9a-f]{16}$", ControlProtocol.PipeName(1, db));
    }

    [Fact]
    public void Nom_du_mutex_par_base_dans_la_session()
    {
        string db = Path.Combine(Path.GetTempPath(), "KeyStats", "keystats.db");
        Assert.Equal(ControlProtocol.MutexName(db), ControlProtocol.MutexName(db.ToUpperInvariant()));
        Assert.NotEqual(ControlProtocol.MutexName(db), ControlProtocol.MutexName(db + "2"));
        Assert.Matches("^Local\\\\KeyStats\\.Agent\\.[0-9a-f]{16}$", ControlProtocol.MutexName(db));
    }

    [Fact]
    public void Relance_sans_argument_sur_la_base_par_defaut_sinon_avec_db()
    {
        string defaultDb = Path.Combine(Path.GetTempPath(), "KeyStats", "keystats.db");
        Assert.Empty(ControlProtocol.StartArguments(defaultDb.ToUpperInvariant(), defaultDb));
        string other = Path.Combine(Path.GetTempPath(), "dev", "keystats.db");
        Assert.Equal(["--db", other], ControlProtocol.StartArguments(other, defaultDb));
    }
}
