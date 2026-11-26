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

    [Fact]
    public void Nom_du_tube_par_base_et_par_session()
    {
        string db = Path.Combine(Path.GetTempPath(), "KeyStats", "keystats.db");
        Assert.Equal(ControlProtocol.PipeName(1, db), ControlProtocol.PipeName(1, db.ToUpperInvariant()));
        Assert.NotEqual(ControlProtocol.PipeName(1, db), ControlProtocol.PipeName(2, db));
        Assert.NotEqual(ControlProtocol.PipeName(1, db), ControlProtocol.PipeName(1, db + "2"));
        Assert.Matches("^KeyStats\\.Agent\\.1\\.[0-9a-f]{16}$", ControlProtocol.PipeName(1, db));
    }
}
