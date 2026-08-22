using System.IO;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former ConsoleSessionSelfTest (Wave 4): the supervised
/// recovery console session must scope its reader/writer to the injected
/// streams rather than process-wide console handles.
/// </summary>
public class ConsoleSessionTests
{
    [Fact]
    public void Session_ReadsAndWritesOnlyTheScopedStreams()
    {
        using var input = new StringReader("answer\n");
        using var output = new StringWriter();

        using ConsoleSession session = ConsoleSession.ForTesting(input, output);
        session.Output.Write("prompt: ");
        session.Output.Flush();

        Assert.Equal("answer", session.Input.ReadLine());
        Assert.Equal("prompt: ", output.ToString());
    }
}
