using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NoFences.Util
{
    public static class WildcardMatcher
    {
        /// <summary>
        /// Checks a file name against semicolon-separated wildcard patterns, e.g. "*.png; screenshot*".
        /// </summary>
        public static bool MatchesAny(string patterns, string fileName)
        {
            if (string.IsNullOrWhiteSpace(patterns) || string.IsNullOrEmpty(fileName))
                return false;

            return patterns.Split(';')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Any(p => Matches(p, fileName));
        }

        private static bool Matches(string pattern, string fileName)
        {
            var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase);
        }
    }
}
