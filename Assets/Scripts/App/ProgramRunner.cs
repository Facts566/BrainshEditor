using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Brainsh;
using UnityEngine;

namespace BrainshEditor
{
    /// <summary>
    /// Runs a compiled program on a background thread and streams the output
    /// back to the interface. Input for ',' blocks the machine thread until
    /// the user types a line.
    /// </summary>
    [AddComponentMenu("")]
    public class ProgramRunner : MonoBehaviour
    {
        [Tooltip("Commands the program may execute before it is stopped.")]
        public long StepLimit = 50_000_000L;

        public bool IsRunning { get; private set; }

        /// <summary>Raised on the main thread with new program output.</summary>
        public event Action<string> OutputReady;

        /// <summary>Raised on the main thread when the run ends. Null message means success.</summary>
        public event Action<string> Finished;

        private BrainshMachine _machine;
        private ConsoleIo _io;
        private Thread _thread;

        private volatile bool _finished;
        private string _finishMessage;
        private bool _finishIsError;

        private void Update()
        {
            if (_io != null)
            {
                string chunk = _io.Drain();
                if (!string.IsNullOrEmpty(chunk) && OutputReady != null) OutputReady(chunk);
            }

            if (!_finished) return;

            bool wasError = _finishIsError;
            string message = _finishMessage;

            _finished = false;
            IsRunning = false;

            if (Finished != null) Finished(wasError ? message : null);
        }

        public void StartProgram(BrainshProgram program, ConsoleIo io)
        {
            Stop();

            _io = io;
            _machine = new BrainshMachine(program, io, StepLimit);
            _finished = false;
            _finishMessage = null;
            _finishIsError = false;
            IsRunning = true;

            _thread = new Thread(RunProgram);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Stop()
        {
            if (_machine != null) _machine.Cancel();
            if (_io != null) _io.Cancel();

            Thread thread = _thread;
            _thread = null;

            if (thread != null && thread.IsAlive)
            {
                // Give the machine a moment to notice the cancel flag.
                thread.Join(300);
            }

            IsRunning = false;
        }

        private void RunProgram()
        {
            try
            {
                _machine.Run();
                Complete(null, false);
            }
            catch (BrainshCancelledException)
            {
                Complete("stopped", false);
            }
            catch (BrainshException e)
            {
                Complete(e.FullMessage, true);
            }
            catch (Exception e)
            {
                Complete("internal error: " + e.Message, true);
            }
        }

        private void Complete(string message, bool isError)
        {
            _finishMessage = message;
            _finishIsError = isError;
            _finished = true;
        }
    }

    /// <summary>Console implementation of the machine input / output.</summary>
    public sealed class ConsoleIo : IBrainshIO
    {
        private readonly ConcurrentQueue<char> _output = new ConcurrentQueue<char>();
        private readonly Queue<int> _input = new Queue<int>();
        private readonly object _inputLock = new object();

        private int _written;
        private volatile bool _cancelled;
        private volatile bool _waitingForInput;

        public bool IsCancelled
        {
            get { return _cancelled; }
        }

        /// <summary>True while the machine waits for the user to type a line.</summary>
        public bool WaitingForInput
        {
            get { return _waitingForInput; }
        }

        public int OutputLimit { get; set; } = 200_000;

        public void Write(char c)
        {
            if (++_written > OutputLimit)
                throw new BrainshException(BrainshErrorKind.Limit,
                    "output limit reached, the program prints in an endless loop");

            _output.Enqueue(c);
        }

        public void WriteNumber(int value)
        {
            string text = value.ToString();
            for (int i = 0; i < text.Length; i++) Write(text[i]);
        }

        /// <summary>Blocks until the user submits a line, or returns false when cancelled.</summary>
        public bool TryReadChar(out int value)
        {
            while (true)
            {
                lock (_inputLock)
                {
                    SkipWhiteSpace();

                    if (_input.Count > 0)
                    {
                        _waitingForInput = false;
                        value = _input.Dequeue();
                        return true;
                    }

                    if (_cancelled)
                    {
                        _waitingForInput = false;
                        value = 0;
                        return false;
                    }

                    _waitingForInput = true;
                    Monitor.Wait(_inputLock, 50);
                }
            }
        }

        public void PushInput(string line)
        {
            lock (_inputLock)
            {
                foreach (char c in line ?? string.Empty) _input.Enqueue(c);
                Monitor.PulseAll(_inputLock);
            }
        }

        public void Cancel()
        {
            lock (_inputLock)
            {
                _cancelled = true;
                _waitingForInput = false;
                Monitor.PulseAll(_inputLock);
            }
        }

        public string Drain()
        {
            if (_output.IsEmpty) return null;

            var sb = new System.Text.StringBuilder(256);

            while (_output.TryDequeue(out char c))
            {
                sb.Append(c);
                if (sb.Length >= 4096) break;
            }

            return sb.ToString();
        }

        public void ClearOutput()
        {
            while (_output.TryDequeue(out _))
            {
            }
        }

        private void SkipWhiteSpace()
        {
            while (_input.Count > 0)
            {
                int head = _input.Peek();
                if (head != ' ' && head != '\t' && head != '\n' && head != '\r') break;
                _input.Dequeue();
            }
        }
    }
}