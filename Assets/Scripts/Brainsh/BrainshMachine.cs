using System;

namespace Brainsh
{
    /// <summary>
    /// The Brainsh virtual machine. Pure C#, safe to run on a background thread.
    /// Semantics follow the reference interpreter byte for byte.
    /// </summary>
    public sealed class BrainshMachine
    {
        public const int TapeSize = 30000;

        private readonly BrainshProgram _program;
        private readonly IBrainshIO _io;
        private readonly long _stepLimit;
        private readonly byte[] _tape = new byte[TapeSize];
        private readonly Random _random;

        private volatile bool _cancel;
        private int _ptr;
        private int _pc;
        private long _steps;

        public BrainshMachine(BrainshProgram program, IBrainshIO io, long stepLimit = 0)
        {
            _program = program ?? throw new ArgumentNullException(nameof(program));
            _io = io;
            _stepLimit = stepLimit;
            _random = new Random(Environment.TickCount ^ DateTime.UtcNow.Millisecond);
        }

        public bool IsCancelled => _cancel;

        public long Steps => _steps;

        public int Pointer => _ptr;

        public byte Cell(int index)
        {
            if (index < 0 || index >= TapeSize) return 0;
            return _tape[index];
        }

        public void Cancel()
        {
            _cancel = true;
        }

        public void Run()
        {
            if (_io == null) throw new ArgumentNullException(nameof(_io));

            string code = _program.Code;
            int length = code.Length;
            int[] forward = _program.JumpForward;
            int[] backward = _program.JumpBack;

            byte[] tape = _tape;
            int pc = 0;
            _ptr = 0;
            _pc = 0;
            _steps = 0;

            while (pc < length)
            {
                _steps++;
                _pc = pc;

                if ((_steps & 511) == 0)
                {
                    if (_cancel) throw new BrainshCancelledException();
                    if (_stepLimit > 0 && _steps > _stepLimit)
                        throw Error(BrainshErrorKind.Limit,
                            "the program ran longer than the step limit (" + _stepLimit + ")");
                }

                switch (code[pc])
                {
                    case '>':
                        _ptr++;
                        break;
                    case '<':
                        _ptr--;
                        break;
                    case '+':
                        tape[_ptr]++;
                        break;
                    case '-':
                        tape[_ptr]--;
                        break;

                    case 'm':
                        if (_ptr + 1 >= TapeSize)
                            throw Error(BrainshErrorKind.Runtime, "'m' needs a cell on the right");

                        tape[_ptr] = (byte)(tape[_ptr] * tape[_ptr + 1]);
                        break;

                    case 'r':
                        tape[_ptr] = (byte)(1 + _random.Next(255));
                        break;

                    case '.':
                        _io.Write((char)tape[_ptr]);
                        break;

                    case ',':
                    {
                        int value;
                        if (_io.TryReadChar(out value))
                        {
                            tape[_ptr] = (byte)value;
                            break;
                        }

                        // No input left: the cell keeps its value, as with the reference
                        // interpreter. A cancelled run ends here.
                        if (_io.IsCancelled) throw new BrainshCancelledException();
                        break;
                    }

                    case '#':
                        _io.WriteNumber(tape[_ptr]);
                        _io.Write('\n');
                        break;

                    case 'N':
                        tape[_ptr] -= 48;
                        break;
                    case 'S':
                        tape[_ptr] += 48;
                        break;

                    case 'd':
                        if (_ptr + 1 >= TapeSize)
                            throw Error(BrainshErrorKind.Runtime, "'d' needs a cell on the right");

                        tape[_ptr + 1] = (byte)(tape[_ptr] % 10);
                        break;

                    case ':':
                        tape[_ptr] += ReadCount(ref pc);
                        break;

                    case ';':
                        tape[_ptr] -= ReadCount(ref pc);
                        break;

                    case '[':
                        if (tape[_ptr] == 0) pc = forward[pc];
                        break;

                    case '(':
                        if (tape[_ptr] < 48 || tape[_ptr] > 57) pc = forward[pc];
                        break;

                    case ']':
                        if (tape[_ptr] != 0) pc = backward[pc];
                        break;
                }

                if (_ptr < 0 || _ptr >= TapeSize)
                    throw Error(BrainshErrorKind.Runtime, "the pointer left the tape");

                pc++;
            }

            if (_cancel) throw new BrainshCancelledException();
        }

        /// <summary>
        /// Reads the number of a ":N" / ";N" command and leaves pc on its last digit.
        /// Cell values wrap at 256, so adding count times is the same as adding count % 256.
        /// </summary>
        private byte ReadCount(ref int pc)
        {
            string code = _program.Code;
            int i = pc + 1;

            if (i >= code.Length || IsDigit(code[i]) == false)
                throw Error(BrainshErrorKind.Runtime, "a number is required after '" + code[pc] + "'");

            int count = 0;
            while (i < code.Length && IsDigit(code[i]))
            {
                count = (count * 10 + (code[i] - '0')) % 256;
                i++;
            }

            pc = i - 1;
            return (byte)count;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private BrainshException Error(BrainshErrorKind kind, string message)
        {
            return new BrainshException(kind, message, _program.FileAt(_pc), _program.LineAt(_pc));
        }
    }
}