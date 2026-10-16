namespace BrainshEditor
{
    /// <summary>Sample programs written into the store on the first launch.</summary>
    public static class Examples
    {
        public sealed class Example
        {
            public string FileName;
            public string Source;

            public Example(string fileName, string source)
            {
                FileName = fileName;
                Source = source;
            }
        }

        public static Example[] All
        {
            get
            {
                return new[]
                {
                    new Example("hello.bsh", Hello),
                    new Example("digits.bsh", Digits),
                    new Example("math.bsh", Math),
                    new Example("lib.bsh", Lib),
                    new Example("calc.bsh", Calc),
                    new Example("add_digits.bsh", AddDigits),
                    new Example("password.bsh", Password)
                };
            }
        }

        /// <summary>Writes the examples that are not in the store yet.</summary>
        public static void Seed(ScriptStore store)
        {
            foreach (Example example in All)
                if (!store.Exists(example.FileName)) store.Write(example.FileName, example.Source);
        }

        private const string Hello =
            ":72. { H }\n" +
            ">:101. { e }\n" +
            ">:108.. { ll }\n" +
            ">:111. { o }\n" +
            ">:44. { , }\n" +
            ">:32. { space }\n" +
            ">:119. { w }\n" +
            ">:111. { o }\n" +
            ">:114. { r }\n" +
            ">:108. { l }\n" +
            ">:100. { d }\n" +
            ">:33. { ! }\n" +
            ">:10. { line break }";

        private const string Digits =
            "{ () is a condition: the block runs ONCE if the cell holds a digit (48-57)\n" +
            "  Note: :N adds N, ;N subtracts N - these are not assignments! }\n" +
            "\n" +
            ":52(.)      { 52 = '4' is a digit, so '4' is printed }\n" +
            ":81(.)      { 52 + 81 = 133 is not a digit, the block is skipped }\n" +
            ";133        { cell holds 133, reset it }\n" +
            "\n" +
            ":48>:5\n" +
            "[\n" +
            "  <(. :2)>;1   { print 0 2 4 6 8 }\n" +
            "]\n" +
            "\n" +
            "<;58:49>:5\n" +
            "[\n" +
            "  <(. :2)>;1   { print 1 3 5 7 9 }\n" +
            "]";

        private const string Lib =
            "{ A reusable library: include it with %lib.bsh }\n" +
            "\n" +
            "{ @put - print the character in the current cell, then clear it }\n" +
            "@put = .[-]\n" +
            "\n" +
            "@newline = :10@put\n" +
            "\n" +
            "@hi = :72@put:105@put@newline\n" +
            "\n" +
            "@countdown = :51@put:50@put:49@put@newline\n" +
            "\n" +
            "@square = m >[-] <";

        private const string Math =
            "{ Commands: m, r and %include }\n" +
            "\n" +
            "%lib.bsh\n" +
            "\n" +
            "@hi\n" +
            "@countdown\n" +
            "\n" +
            "{ m - multiply the current cell by the cell on the right: 5 * 7 = 35 }\n" +
            ":5 >:7 < m #\n" +
            "\n" +
            "{ values wrap at 256: 255 * 3 = 765 becomes 253 }\n" +
            ">[-] :3 <[-] :255 m #\n" +
            "\n" +
            "{ r - a random number from 1 to 255, different on every run }\n" +
            "[-] r #\n" +
            "\n" +
            "{ @square from the library: 6 * 6 = 36 }\n" +
            ">[-] :6 <[-] :6 @square #";

        private const string Calc =
            "@add = [-<+>]\n" +
            "\n" +
            "{ type two two digit numbers, for example 12 and 34 }\n" +
            ",N>:10<m>,N\n" +
            "@add\n" +
            ",N>:10<m>,N\n" +
            "@add\n" +
            "@add\n" +
            "<@add\n" +
            "<#";

        private const string AddDigits =
            "@add = [->+<]\n" +
            "\n" +
            ",N>,N\n" +
            "<@add>#";

        private const string Password =
            ">>>+>+<<<<\n" +
            "\n" +
            ",;49[[-]>>>[-]<<<]\n" +
            ">,;50[[-]>>[-]<<]\n" +
            ">,;51[[-]>[-]<]\n" +
            "\n" +
            ">[- >[-] >:79.;75.[-]:10. <<]\n" +
            ">[- >:78.;79.;84.[-]:32.:65.[-]:67.[-]:68.[-]:10. <]";
    }
}