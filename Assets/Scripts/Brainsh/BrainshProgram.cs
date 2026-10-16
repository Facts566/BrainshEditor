namespace Brainsh
{
    /// <summary>Reads files referenced by the %include directive.</summary>
    public interface IBrainshFileProvider
    {
        bool TryReadFile(string path, out string content);
    }

    /// <summary>
    /// Compiled Brainsh program: a flat command stream plus the source position of every
    /// command and precomputed bracket jumps.
    /// </summary>
    public sealed class BrainshProgram
    {
        public string Code { get; internal set; } = string.Empty;
        public int[] LineOf { get; internal set; } = new int[0];
        public int[] FileOf { get; internal set; } = new int[0];
        public string[] FileNames { get; internal set; } = new string[0];

        /// <summary>'[' and '(' point at the matching ']' / ')'.</summary>
        public int[] JumpForward { get; internal set; } = new int[0];

        /// <summary>']' and ')' point at the matching '[' / '('.</summary>
        public int[] JumpBack { get; internal set; } = new int[0];

        public int Length => Code.Length;

        public string FileAt(int index)
        {
            if (FileOf == null || index < 0 || index >= FileOf.Length) return null;
            int file = FileOf[index];
            if (file < 0 || file >= FileNames.Length) return null;
            return FileNames[file];
        }

        public int LineAt(int index)
        {
            if (LineOf == null || index < 0 || index >= LineOf.Length) return 0;
            return LineOf[index];
        }
    }
}