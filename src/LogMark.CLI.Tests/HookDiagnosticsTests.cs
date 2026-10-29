using System;
using System.IO;
using FluentAssertions;
using LogMark.CLI.Commands;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class HookDiagnosticsTests : IDisposable
{
    private readonly Func<string?> _originalDebugVar;
    private readonly TextWriter _originalErr;

    public HookDiagnosticsTests()
    {
        _originalDebugVar = HookDiagnostics.DebugVar;
        _originalErr = Console.Error;
    }

    public void Dispose()
    {
        HookDiagnostics.DebugVar = _originalDebugVar;
        Console.SetError(_originalErr);
    }

    [Fact]
    public void Warn_Suppressed_WhenDebugUnset()
    {
        HookDiagnostics.DebugVar = () => null;
        var sw = new StringWriter();
        Console.SetError(sw);

        HookDiagnostics.Warn("agent-context: Vault path not set.");

        sw.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Warn_Emitted_WhenDebugSet()
    {
        HookDiagnostics.DebugVar = () => "1";
        var sw = new StringWriter();
        Console.SetError(sw);

        HookDiagnostics.Warn("agent-context: boom");

        sw.ToString().Should().Contain("agent-context: boom");
    }
}
