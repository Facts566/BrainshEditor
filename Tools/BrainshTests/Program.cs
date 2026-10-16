using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Brainsh;

internal sealed class TestIO : IBrainshIO
{
    private readonly Queue<int> _input = new Queue<int>();
    public readonly StringBuilder Output = new StringBuilder();

    public bool IsCancelled => false;

    public TestIO(string input)
    {
        foreach (char c in input ?? string.Empty) _input.Enqueue(c);
    }

    public void Write(char c) => Output.Append(c);

    public void WriteNumber(int value) => Output.Append(value.ToString(CultureInfo.InvariantCulture));

    // Mirrors std::cin >> char: white space is skipped, and when the input ends the
    // reference interpreter leaves the cell untouched.
    public bool TryReadChar(out int value)
    {
        while (_input.Count > 0 && (_input.Peek() == ' ' || _input.Peek() == '\t' ||
                                   _input.Peek() == '\n' || _input.Peek() == '\r'))
            _input.Dequeue();

        if (_input.Count == 0)
        {
            value = 0;
            return false;
        }

        value = _input.Dequeue();
        return true;
    }
}

/// <summary>
/// Runs the Brainsh compiler and machine against the reference interpreter from
/// https://github.com/Facts566/Brainsh and compares the output byte for byte.
///
/// Safety: the reference is a Brainfuck interpreter, so an endless loop is a normal
/// program for it. Every child process is killed after a timeout and our own machine
/// runs with a step limit, so the suite can never hang the machine.
/// </summary>
internal static class Program
{
    private const int ProcessTimeoutMs = 10_000;
    private const long StepLimit = 50_000_000L;

    private static int _failed;
    private static int _passed;

    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    private static int Main()
    {
        // Valid programs: exit code and output must match the reference byte for byte.
        Diff("hello", "");
        Diff("digits", "");
        Diff("add_digits", "35");
        Diff("calc", "1234");
        Diff("password", "123");
        Diff("password", "");
        Diff("lib", "");
        Diff("math_det", "");

        Diff("probe_wrap", "");
        Diff("probe_nums", "");
        Diff("probe_arith", "");
        Diff("probe_cond", "");
        Diff("probe_nested_comment", "");
        Diff("probe_comments_inline", "");
        Diff("probe_include_twice", "");
        Diff("probe_macros", "");
        Diff("probe_loops", "");
        Diff("probe_eof", "");
        Diff("probe_input_ws", "  7\n 9");
        Diff("probe_eof_mid_loop", "");
        Diff("probe_tape_end", "");

        // Invalid programs: both implementations must refuse them, with the same output.
        Fail("err_unknown_macro");
        Fail("err_invalid_char");
        Fail("err_unclosed_bracket");
        Fail("err_stray_close");
        Fail("err_unclosed_comment");
        Fail("err_stray_comment_end");
        Fail("err_number_after_colon");
        Fail("err_self_macro");
        Fail("err_pointer_left");
        Fail("err_missing_include");
        Fail("err_unclosed_paren");
        Fail("err_runaway");

        Extensions();
        RandomRange();
        HugeCount();
        RunawayLoop();
        ErrorMessages();
        LineReporting();

        Console.WriteLine();
        Console.WriteLine("passed: " + _passed + "   failed: " + _failed);
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>Behaviour the C++ reference rejects but the editor accepts on purpose.</summary>
    private static void Extensions()
    {
        Expect("underscores in macro names (official VS Code grammar)",
            "@my_macro = .[-]\n:72@my_macro:105@my_macro", "", "Hi");
        Expect("quoted include path", "%\"lib.bsh\"\n@hi", "", "Hi\n");
        Expect("CRLF line endings", ":72.\r\n[-]:105.\r\n", "", "Hi");
        Expect("space before '=' in a macro", "@put  = .[-]\n:72@put:105@put", "", "Hi");
        Expect("include resolved next to the script", "%lib.bsh\n@countdown", "", "321\n");
        Expect("empty program", "", "", "");
    }

    private static void Expect(string what, string source, string input, string expected)
    {
        try
        {
            var io = new TestIO(input);
            new BrainshMachine(BrainshCompiler.Compile(source, new DiskFileProvider("probes"), "main.bsh"),
                io, StepLimit).Run();

            string actual = io.Output.ToString();
            Report(what + "  ->  " + Escape(actual), actual == expected);
        }
        catch (Exception e)
        {
            Report(what + "  ->  threw " + e.Message, false);
        }
    }

    private static void Diff(string name, string input)
    {
        string path = Path.Combine("probes", name + ".bsh");
        string source = File.ReadAllText(path);

        Compare(name, RunReference(path, input), RunMine(path, source, input));
    }

    private static void Fail(string name)
    {
        string path = Path.Combine("probes", name + ".bsh");
        string source = File.ReadAllText(path);

        Result reference = RunReference(path, string.Empty);
        Result mine = RunMine(path, source, string.Empty);

        if (reference.ExitCode == 0 && mine.ExitCode == 0)
        {
            _failed++;
            Console.WriteLine("FAIL  " + name + "  expected both to reject it, but both exited with 0");
            return;
        }

        Compare(name + " (both reject)", reference, mine);
    }

    private static void Compare(string name, Result reference, Result mine)
    {
        if (reference.TimedOut || mine.TimedOut)
        {
            _failed++;
            Console.WriteLine("FAIL  " + name + "  timed out after " + (ProcessTimeoutMs / 1000) + "s");
            return;
        }

        bool bothRejected = reference.ExitCode != 0 && mine.ExitCode != 0;

        if (reference.ExitCode == mine.ExitCode && reference.SameBytes(mine))
        {
            _passed++;
            Console.WriteLine("ok    " + name + "  ->  " + (bothRejected ? "(rejected) " : "") + Escape(mine.Text));
        }
        else
        {
            _failed++;
            Console.WriteLine("FAIL  " + name);
            Console.WriteLine("      reference exit=" + reference.ExitCode + ": " + Escape(reference.Text));
            Console.WriteLine("      mine      exit=" + mine.ExitCode + ": " + Escape(mine.Text));
        }
    }

    private static Result RunReference(string path, string input)
    {
        var psi = new ProcessStartInfo("ref/bsh", "\"" + path + "\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using (Process process = Process.Start(psi))
        {
            process.StandardInput.Write(input ?? string.Empty);
            process.StandardInput.Close();

            var buffer = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(buffer);
            string error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                Kill(process);
                return new Result(1, new byte[0], "timeout", true);
            }

            return new Result(process.ExitCode, buffer.ToArray(), error.Trim());
        }
    }

    private static Result RunMine(string path, string source, string input)
    {
        var io = new TestIO(input);

        try
        {
            var provider = new DiskFileProvider(Path.GetDirectoryName(Path.GetFullPath(path)));
            BrainshProgram program = BrainshCompiler.Compile(source, provider, Path.GetFileName(path));

            new BrainshMachine(program, io, StepLimit).Run();

            return new Result(0, Latin1.GetBytes(io.Output.ToString()), "");
        }
        catch (BrainshCancelledException)
        {
            return new Result(1, Latin1.GetBytes(io.Output.ToString()), "stopped");
        }
        catch (BrainshException e)
        {
            return new Result(1, Latin1.GetBytes(io.Output.ToString()), e.FullMessage);
        }
        catch (Exception e)
        {
            return new Result(2, Latin1.GetBytes(io.Output.ToString()),
                "CRASH " + e.GetType().Name + ": " + e.Message);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }
    }

    private static void RandomRange()
    {
        BrainshProgram program = BrainshCompiler.Compile("[-]r#", null);
        bool ok = true;

        for (int i = 0; i < 500; i++)
        {
            var io = new TestIO(string.Empty);
            new BrainshMachine(program, io, StepLimit).Run();

            int value = int.Parse(io.Output.ToString().Trim(), CultureInfo.InvariantCulture);
            if (value < 1 || value > 255) ok = false;
        }

        Report("random 'r' stays in 1..255", ok);
    }

    private static void HugeCount()
    {
        // The reference loops "count" times; we reduce modulo 256 immediately.
        try
        {
            var io = new TestIO(string.Empty);
            new BrainshMachine(BrainshCompiler.Compile(":999999999999999999999999999#", null), io, StepLimit).Run();

            Report("huge :N count wraps at once  ->  " + io.Output.ToString().Trim(), true);
        }
        catch (Exception e)
        {
            Report("huge :N count wraps at once (" + e.Message + ")", false);
        }
    }

    /// <summary>
    /// An endless loop is a normal program here. The editor must stop it with the step
    /// limit instead of freezing the device. The reference interpreter has no such limit,
    /// so it is deliberately not started for this test.
    /// </summary>
    private static void RunawayLoop()
    {
        string path = Path.Combine("probes", "probe_runaway_loop.bsh");
        Result mine = RunMine(path, File.ReadAllText(path), string.Empty);

        bool stopped = !mine.TimedOut && mine.ExitCode != 0 && mine.Message.Contains("step limit");
        Report("endless loop is stopped by the step limit  ->  " + mine.Message, stopped);
    }

    private static void ErrorMessages()
    {
        CheckCompileMessage("invalid character reports its line", ":72.\n?\n:73.", "main.bsh:2");
        CheckCompileMessage("unclosed bracket reports its line", ":72.\n[\n", "main.bsh:2");
        CheckCompileMessage("unknown macro", "@nope", "@nope");
        CheckCompileMessage("self recursive macro", "@a = @a\n@a", "calls itself");
        CheckCompileMessage("unclosed comment", "{ oops", "unclosed '{'");
        CheckCompileMessage("missing include", "%nope.bsh", "cannot open file");
        CheckRunMessage("number after ':'", ":72.\n:#", "a number is required after ':'");
        CheckRunMessage("number after ';'", ":72.\n;", "a number is required after ';'");
        CheckRunMessage("m needs a cell on the right", new string('>', 29999) + "m", "'m' needs a cell on the right");
    }

    private static void LineReporting()
    {
        CheckRunLine("runtime error keeps its line", ":72.\n+\n<", "main.bsh", 3);
        CheckRunLine("macro body keeps its own line", "@bad = <\n@bad", "main.bsh", 1);
    }

    private static void CheckCompileMessage(string what, string source, string expectedPart)
    {
        try
        {
            BrainshCompiler.Compile(source, null, "main.bsh");
            Report(what, false);
        }
        catch (BrainshException e)
        {
            Report(what + "  ->  " + e.FullMessage, e.FullMessage.Contains(expectedPart));
        }
    }

    private static void CheckRunMessage(string what, string source, string expectedPart)
    {
        try
        {
            var io = new TestIO(string.Empty);
            new BrainshMachine(BrainshCompiler.Compile(source, null, "main.bsh"), io, StepLimit).Run();
            Report(what, false);
        }
        catch (BrainshException e)
        {
            Report(what + "  ->  " + e.FullMessage, e.FullMessage.Contains(expectedPart));
        }
    }

    private static void CheckRunLine(string what, string source, string file, int expectedLine)
    {
        try
        {
            var io = new TestIO(string.Empty);
            new BrainshMachine(BrainshCompiler.Compile(source, null, file), io, StepLimit).Run();
            Report(what, false);
        }
        catch (BrainshException e)
        {
            Report(what + "  ->  " + e.FullMessage, e.Line == expectedLine);
        }
    }

    private static void Report(string what, bool ok)
    {
        if (ok)
        {
            _passed++;
            Console.WriteLine("ok    " + what);
        }
        else
        {
            _failed++;
            Console.WriteLine("FAIL  " + what);
        }
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder();

        foreach (char c in value)
        {
            if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 32 || c > 126) sb.Append("\\x").Append(((int)c).ToString("X2"));
            else sb.Append(c);
        }

        return sb.ToString();
    }

    private sealed class Result
    {
        public readonly int ExitCode;
        public readonly byte[] Bytes;
        public readonly string Message;
        public readonly bool TimedOut;

        public Result(int exitCode, byte[] bytes, string message, bool timedOut = false)
        {
            ExitCode = exitCode;
            Bytes = bytes;
            Message = message;
            TimedOut = timedOut;
        }

        public string Text => Latin1.GetString(Bytes);

        public bool SameBytes(Result other)
        {
            if (Bytes.Length != other.Bytes.Length) return false;

            for (int i = 0; i < Bytes.Length; i++)
                if (Bytes[i] != other.Bytes[i]) return false;

            return true;
        }
    }

    private sealed class DiskFileProvider : IBrainshFileProvider
    {
        private readonly string _directory;

        public DiskFileProvider(string directory)
        {
            _directory = directory;
        }

        public bool TryReadFile(string path, out string content)
        {
            try
            {
                content = File.ReadAllText(Path.Combine(_directory, path));
                return true;
            }
            catch (Exception)
            {
                content = null;
                return false;
            }
        }
    }
}