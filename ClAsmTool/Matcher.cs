using System;
using System.Linq;

namespace Lomont.ClAsmTool
{
    public static class Matcher
    {

        public static bool MatchNumber(string text, int start, out int length)
        {
            length = 0;
            var next = text[start];
            var ns = text.Substring(start);
            var ishex2 = ns.ToLower().StartsWith("0x");
            var ishex1 = next == '$';
            if (Char.IsDigit(next) || ishex1 || ishex2)
            {
                var index = start;
                if (ishex1)
                    index++;
                if (ishex2)
                    index += 2;
                Func<char, bool> isDigit = Char.IsDigit;
                if (ishex1 || ishex2)
                    isDigit = c => Char.IsDigit(c) || "abcdef".Contains(Char.ToLower(c));

                while (index < text.Length && isDigit(text[index]))
                    ++index;
                length = index - start;
                return true;
            }
            return false;
        }

        static bool IsSymbolChar(char ch, bool firstChar)
        {
            if (Char.IsLetter(ch) || ch == '_' || ch=='.' || ch=='#')
                return true;
            if (!firstChar && Char.IsDigit(ch))
                return true;
            return false;
        }

        // matches symbols with optional '.' to separate fields
        public static bool MatchStructField(string text, int start, out int length)
        {
            length = 0;
            var i = start;
            while (i < text.Length && (IsSymbolChar(text[i], i == start) || text[i] == '.'))
                ++i;
            if (i > start)
            {
                length = i - start;
                return true;
            }
            return false;

        }

        public static bool MatchSymbol(string text, int start, out int length)
        {
            length = 0;
            var i = start;
            while (i < text.Length && IsSymbolChar(text[i], i==start))
                ++i;
            if (i > start)
            {
                length = i - start;
                return true;
            }
            return false;

        }

        // match '\' char
        public static bool MatchLineContinue(string text, int start, out int length)
        {
            length = 0;
            if (text[start] == LINE_CONTINUATION)
            {
                length = 1;
                return true;
            }
            return false;
        }

        static char LINE_CONTINUATION = '\\';

        // match ':' char
        public static bool MatchDelimiter(string text, int start, out int length)
        {
            length = 0;
            if (text[start] == ':')
            {
                length = 1;
                return true;
            }
            return false;
        }

        // match any except comment
        public static bool MatchMisc(string text, int start, out int length)
        {
            length = 0;
            var i = start;
            var inString = false;
            while (i < text.Length && !MatchComment(text, i, out _))
            {
                if (text[i] == '"')
                    inString = !inString;
                if (text[i] == LINE_CONTINUATION && !inString)
                    break;
                i++;
            }
            if (i > start)
            {
                length = i - start;
                return true;
            }
            return false;
        }

        public static bool MatchComment(string text, int start, out int length)
        {
            length = 0;
            if (text[start] != COMMENT_CHAR)
                return false;
            length = text.Length - start;
            return true;
        }

        public static bool MatchWhitespace(string text, int start, out int length)
        {
            length = 0;
            var i = start;
            while (i < text.Length && whitespace.Contains(text[i]))
                ++i;
            if (i > start)
            {
                length = i - start;
                return true;
            }
            return false;
        }


        static char COMMENT_CHAR = ';';
        static string whitespace = " \t\r\n";
    }
}
