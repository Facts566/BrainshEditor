using System;

namespace Brainsh
{
    public enum BrainshErrorKind
    {
        Syntax,
        Runtime,
        Io,
        Limit,
        Cancelled
    }

    /// <summary>Any error produced by the Brainsh compiler or the virtual machine.</summary>
    public class BrainshException : Exception
    {
        public BrainshErrorKind Kind { get; }
        public string File { get; }
        public int Line { get; }

        public BrainshException(BrainshErrorKind kind, string message, string file = null, int line = 0)
            : base(message)
        {
            Kind = kind;
            File = file;
            Line = line;
        }

        /// <summary>Message with the "file:line" prefix when the position is known.</summary>
        public string FullMessage
        {
            get
            {
                if (string.IsNullOrEmpty(File)) return Message;
                if (Line > 0) return File + ":" + Line + ": " + Message;
                return File + ": " + Message;
            }
        }
    }

    /// <summary>Thrown when the user stops a running program.</summary>
    public sealed class BrainshCancelledException : BrainshException
    {
        public BrainshCancelledException() : base(BrainshErrorKind.Cancelled, "stopped")
        {
        }
    }
}