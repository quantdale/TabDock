using System;
using System.IO;
using System.Text;

namespace TabDock.Services;

/// <summary>
/// Owns one interactive diagnostic command's console lifetime. A WinExe has
/// no console by default, so this attaches to the launching console only when
/// redirected standard handles are not already usable.
/// </summary>
internal sealed class ConsoleSession : IDisposable
{
    private readonly TextReader _previousInput;
    private readonly TextWriter _previousOutput;
    private readonly TextWriter _previousError;
    private readonly TextReader? _boundInput;
    private readonly TextWriter? _boundOutput;
    private readonly TextWriter? _boundError;
    private readonly bool _attachedConsole;
    private bool _disposed;

    private ConsoleSession(
        TextReader input,
        TextWriter output,
        TextWriter error,
        bool attachedConsole,
        TextReader? boundInput,
        TextWriter? boundOutput,
        TextWriter? boundError)
    {
        Input = input;
        Output = output;
        Error = error;
        _attachedConsole = attachedConsole;
        _boundInput = boundInput;
        _boundOutput = boundOutput;
        _boundError = boundError;
        _previousInput = Console.In;
        _previousOutput = Console.Out;
        _previousError = Console.Error;
    }

    internal TextReader Input { get; }
    internal TextWriter Output { get; }
    internal TextWriter Error { get; }

    internal static bool TryCreate(out ConsoleSession? session, out string? error)
    {
        session = null;
        error = null;
        bool attached = false;
        TextReader? input = null;
        TextWriter? output = null;
        TextWriter? errorWriter = null;
        try
        {
            if (!HasUsableStandardHandles())
            {
                if (NativeMethods.GetConsoleWindow() == IntPtr.Zero)
                {
                    if (!NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS))
                    {
                        error = "no interactive console or redirected standard input/output is available";
                        return false;
                    }
                    attached = true;
                }
            }

            if (!HasUsableStandardHandles())
            {
                error = "the interactive console does not expose usable standard handles";
                if (attached)
                    NativeMethods.FreeConsole();
                return false;
            }

            input = new StreamReader(
                Console.OpenStandardInput(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            output = new StreamWriter(
                Console.OpenStandardOutput(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            errorWriter = new StreamWriter(
                Console.OpenStandardError(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            ConsoleSession result = new(input, output, errorWriter, attached, input, output, errorWriter);
            Console.SetIn(input);
            Console.SetOut(output);
            Console.SetError(errorWriter);
            session = result;
            return true;
        }
        catch (Exception ex)
        {
            // Late setup (SetIn/SetOut/SetError) can still throw after the
            // streams exist; dispose whatever was created so the underlying
            // standard handles are not leaked.
            try { input?.Dispose(); } catch { }
            try { output?.Dispose(); } catch { }
            try { errorWriter?.Dispose(); } catch { }
            if (attached)
            {
                try { NativeMethods.FreeConsole(); } catch { }
            }
            error = $"console setup failed: {ex.GetType().Name}";
            return false;
        }
    }

    internal static ConsoleSession ForTesting(TextReader input, TextWriter output)
        => new(input, output, output, attachedConsole: false, null, null, null);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { Output.Flush(); } catch { }
        try { Error.Flush(); } catch { }
        Console.SetIn(_previousInput);
        Console.SetOut(_previousOutput);
        Console.SetError(_previousError);
        _boundInput?.Dispose();
        _boundOutput?.Dispose();
        _boundError?.Dispose();
        if (_attachedConsole)
        {
            try { NativeMethods.FreeConsole(); } catch { }
        }
    }

    private static bool HasUsableStandardHandles()
        => IsUsableHandle(NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE))
            && IsUsableHandle(NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE))
            && IsUsableHandle(NativeMethods.GetStdHandle(NativeMethods.STD_ERROR_HANDLE));

    private static bool IsUsableHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return false;
        return NativeMethods.GetFileType(handle) != NativeMethods.FILE_TYPE_UNKNOWN;
    }
}

internal static class ConsoleSessionSelfTest
{
    internal static bool UsesScopedStreams()
    {
        using var input = new StringReader("answer\n");
        using var output = new StringWriter();
        using ConsoleSession session = ConsoleSession.ForTesting(input, output);
        session.Output.Write("prompt: ");
        session.Output.Flush();
        return session.Input.ReadLine() == "answer"
            && output.ToString() == "prompt: ";
    }
}
