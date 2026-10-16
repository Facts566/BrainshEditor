namespace Brainsh
{
    /// <summary>Input / output of a running program.</summary>
    public interface IBrainshIO
    {
        void Write(char c);

        /// <summary>Value printed by '#' (the machine adds the line break itself).</summary>
        void WriteNumber(int value);

        /// <summary>
        /// Produces one character for ','. White space is skipped, like the reference
        /// interpreter's std::cin &gt;&gt; char.
        /// Returns false when no input is left (the cell then keeps its value) or when the
        /// run was cancelled - check <see cref="IsCancelled"/> to tell the two apart.
        /// A real editor blocks here until the user types something.
        /// </summary>
        bool TryReadChar(out int value);

        bool IsCancelled { get; }
    }
}