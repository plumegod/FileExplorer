using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FileExplorer.Tools
{
    public static class WindowsCommandLine
    {
        public static string Encode(IEnumerable<string> arguments)
        {
            if (arguments == null)
                return String.Empty;

            return String.Join(" ", arguments.Select(EncodeArgument));
        }

        private static string EncodeArgument(string argument)
        {
            argument = argument ?? String.Empty;
            if (argument.Length > 0 && argument.All(x => !Char.IsWhiteSpace(x) && x != '"'))
                return argument;

            StringBuilder result = new StringBuilder();
            result.Append('"');
            int backslashes = 0;

            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                }
                else
                {
                    result.Append('\\', backslashes);
                    result.Append(character);
                }

                backslashes = 0;
            }

            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }
    }
}
