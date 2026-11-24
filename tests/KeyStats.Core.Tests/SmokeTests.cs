namespace KeyStats.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Le_projet_de_tests_reference_Core()
    {
        Assert.Equal("KeyStats.Core", typeof(KeyStats.Core.AssemblyMarker).Assembly.GetName().Name);
    }
}
