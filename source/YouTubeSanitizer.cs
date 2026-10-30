using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Barnacle
{
    // No requests are made to the URLs. Uri is used only to validate the authority;
    // the original string is edited directly so other query values stay byte-for-byte.
    public static class YouTubeSanitizer
    {
        private static readonly Regex UrlTokens = new Regex(
            @"(?<![\p{L}\p{N}_./\\:@%?&=+\-])(?<wrappers>(?:\*{1,3}|_{1,3}|~~|\|\|)*)(?<url>(?:https?://|(?:[a-z0-9-]+\.)*(?:youtube\.com|youtube-nocookie\.com|youtu\.be))(?:(?!\)[\[(])[^\s<>""'`])*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private static readonly Regex AdjacentUrl = new Regex(
            @"[,;](?:https?://|(?:[a-z0-9-]+\.)*(?:youtube\.com|youtube-nocookie\.com|youtu\.be))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        public static string Clean(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            // A timeout leaves the whole message unchanged, rather than partly edited.
            try
            {
                return UrlTokens.Replace(text, delegate(Match match)
                {
                    string wrappers = match.Groups["wrappers"].Value;
                    return wrappers + CleanToken(match.Groups["url"].Value, wrappers);
                });
            }
            catch (RegexMatchTimeoutException) { return text; }
        }

        private static string CleanToken(string token, string wrappers)
        {
            // An unseparated comma/semicolon followed by another URL is ambiguous
            // with a URL inside a query value. Leave the whole token unchanged;
            // never delete the second link as part of the first si value.
            if (AdjacentUrl.IsMatch(token)) return token;
            int length = token.Length;
            int closingUnderscores = 0;
            foreach (char wrapper in wrappers) if (wrapper == '_') closingUnderscores++;
            while (length > 0)
            {
                char last = token[length - 1];
                if (last == '_' && closingUnderscores > 0) { closingUnderscores--; length--; }
                else if (IsTrailingPunctuation(last) || last == '*' || last == '~' || last == '|') length--;
                else break;
            }
            if (length == 0) return token;
            string url = token.Substring(0, length);
            if (url.IndexOf('\\') >= 0) return token;

            Uri parsed;
            string qualified = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
            if (!Uri.TryCreate(qualified, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                parsed.UserInfo.Length != 0 || !IsYouTubeHost(parsed.Host)) return token;

            int fragment = url.IndexOf('#');
            int query = url.IndexOf('?');
            if (query < 0 || (fragment >= 0 && query > fragment)) return token;
            int queryEnd = fragment >= 0 ? fragment : url.Length;
            string rawQuery = url.Substring(query + 1, queryEnd - query - 1);
            List<string> values = new List<string>();
            List<string> separators = new List<string>();
            int position = 0;
            string separator = "";
            bool removed = false;
            while (true)
            {
                int ampersand = rawQuery.IndexOf('&', position);
                int end = ampersand >= 0 ? ampersand : rawQuery.Length;
                string parameter = rawQuery.Substring(position, end - position);
                int equals = parameter.IndexOf('=');
                string key = equals >= 0 ? parameter.Substring(0, equals) : parameter;
                if (IsShareIdentifier(key)) removed = true;
                else { values.Add(parameter); separators.Add(separator); }
                if (ampersand < 0) break;
                int separatorLength = ampersand + 5 <= rawQuery.Length &&
                    String.Compare(rawQuery, ampersand, "&amp;", 0, 5, StringComparison.OrdinalIgnoreCase) == 0 ? 5 : 1;
                separator = rawQuery.Substring(ampersand, separatorLength);
                position = ampersand + separatorLength;
            }
            if (!removed) return token;

            StringBuilder result = new StringBuilder(url.Substring(0, query));
            if (values.Count != 0)
            {
                result.Append('?');
                for (int index = 0; index < values.Count; index++)
                {
                    if (index > 0) result.Append(separators[index]);
                    result.Append(values[index]);
                }
            }
            if (fragment >= 0) result.Append(url.Substring(fragment));
            result.Append(token.Substring(length));
            return result.ToString();
        }

        private static bool IsYouTubeHost(string host)
        {
            // A final DNS root dot is valid and does not change the host's identity.
            if (host.EndsWith(".", StringComparison.Ordinal)) host = host.Substring(0, host.Length - 1);
            return String.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(host, "www.youtu.be", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(host, "youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsShareIdentifier(string key)
        {
            try { key = Uri.UnescapeDataString(key); }
            catch (UriFormatException) { return false; }
            return key.Length == 2 && (key[0] == 's' || key[0] == 'S') && (key[1] == 'i' || key[1] == 'I');
        }

        private static bool IsTrailingPunctuation(char value)
        {
            return value == '.' || value == ',' || value == ';' || value == ':' || value == '!' ||
                value == '?' || value == ')' || value == ']' || value == '}';
        }
    }
}
