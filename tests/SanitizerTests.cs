using System;
using Barnacle;

internal static class SanitizerTests
{
    private static int assertions;
    private static int failures;

    private static void Check(string name, string input, string expected)
    {
        string actual = YouTubeSanitizer.Clean(input);
        assertions++;
        if (actual != expected)
        {
            failures++;
            Console.WriteLine("FAIL " + name + "\n  expected: " + expected + "\n  actual:   " + actual);
        }
        assertions++;
        if (YouTubeSanitizer.Clean(actual) != actual)
        {
            failures++;
            Console.WriteLine("FAIL idempotence: " + name);
        }
    }

    public static int Main()
    {
        Check("image example", "https://youtu.be/dQw4w9WgXcQ?si=example-share-token", "https://youtu.be/dQw4w9WgXcQ");
        Check("timestamp first si", "https://youtu.be/dQw4w9WgXcQ?si=abc&t=69", "https://youtu.be/dQw4w9WgXcQ?t=69");
        Check("timestamp last si", "https://youtu.be/dQw4w9WgXcQ?t=69&si=abc", "https://youtu.be/dQw4w9WgXcQ?t=69");
        Check("watch video id", "https://www.youtube.com/watch?v=abc&si=token&t=1m9s", "https://www.youtube.com/watch?v=abc&t=1m9s");
        Check("playlist", "https://www.youtube.com/watch?v=abc&list=PLx&si=token&index=3", "https://www.youtube.com/watch?v=abc&list=PLx&index=3");
        Check("duplicate trackers", "https://youtu.be/abc?si=one&t=3&si=two&list=PL&si=three", "https://youtu.be/abc?t=3&list=PL");
        Check("tracker with equals", "https://youtu.be/abc?si=a=b==&t=4", "https://youtu.be/abc?t=4");
        Check("empty tracker", "https://youtu.be/abc?si=&t=5", "https://youtu.be/abc?t=5");
        Check("key only", "https://youtu.be/abc?si", "https://youtu.be/abc");
        Check("case", "HTTPS://WWW.YouTube.COM/watch?v=a&SI=z&T=5", "HTTPS://WWW.YouTube.COM/watch?v=a&T=5");
        Check("encoded key", "https://youtu.be/abc?%73%69=secret&t=6", "https://youtu.be/abc?t=6");
        Check("encoded values preserved", "https://youtube.com/watch?v=abc&si=x&x=%2f%2F+%26&t=10", "https://youtube.com/watch?v=abc&x=%2f%2F+%26&t=10");
        Check("fragment", "https://youtu.be/abc?si=x&t=5#t=9", "https://youtu.be/abc?t=5#t=9");
        Check("only tracker fragment", "https://youtu.be/abc?si=x#hello", "https://youtu.be/abc#hello");
        Check("fragment tracker not query", "https://youtu.be/abc#?si=x", "https://youtu.be/abc#?si=x");
        Check("timestamp in fragment", "https://youtu.be/abc?si=x#t=69", "https://youtu.be/abc#t=69");
        Check("bare short", "youtu.be/abc?si=x&t=3", "youtu.be/abc?t=3");
        Check("bare full", "www.youtube.com/watch?v=abc&si=x", "www.youtube.com/watch?v=abc");
        Check("mobile", "https://m.youtube.com/watch?v=abc&si=x", "https://m.youtube.com/watch?v=abc");
        Check("music", "https://music.youtube.com/watch?v=abc&si=x", "https://music.youtube.com/watch?v=abc");
        Check("shorts", "https://youtube.com/shorts/abc?si=x", "https://youtube.com/shorts/abc");
        Check("live", "https://youtube.com/live/abc?si=x&t=9", "https://youtube.com/live/abc?t=9");
        Check("embed", "https://www.youtube-nocookie.com/embed/abc?si=x&start=9&end=20", "https://www.youtube-nocookie.com/embed/abc?start=9&end=20");
        Check("root dot", "https://youtube.com./watch?v=a&si=x", "https://youtube.com./watch?v=a");
        Check("port preserved", "https://youtu.be:443/abc?si=x&t=9", "https://youtu.be:443/abc?t=9");
        Check("http", "http://youtu.be/abc?si=x", "http://youtu.be/abc");
        Check("message", "Look at this https://youtu.be/abc?si=x&t=9 please.", "Look at this https://youtu.be/abc?t=9 please.");
        Check("multiple multiline", "first https://youtu.be/abc?si=x\r\nnext https://youtube.com/watch?v=z&si=q\nend", "first https://youtu.be/abc\r\nnext https://youtube.com/watch?v=z\nend");
        Check("markdown", "[video](https://youtu.be/abc?si=x&t=9)", "[video](https://youtu.be/abc?t=9)");
        Check("discord bold", "**https://youtu.be/abc?si=token**", "**https://youtu.be/abc**");
        Check("discord italic", "*https://youtu.be/abc?si=token*", "*https://youtu.be/abc*");
        Check("discord underline", "__https://youtu.be/abc?si=token__", "__https://youtu.be/abc__");
        Check("discord underscore italic", "_https://youtu.be/abc?si=token_", "_https://youtu.be/abc_");
        Check("discord strike", "~~https://youtu.be/abc?si=token~~", "~~https://youtu.be/abc~~");
        Check("discord spoiler", "||https://youtu.be/abc?si=token||", "||https://youtu.be/abc||");
        Check("discord nested", "||__**https://youtu.be/abc?si=token**__||", "||__**https://youtu.be/abc**__||");
        Check("discord nested timestamp", "||__**https://youtu.be/abc?si=token&t=69**__||.", "||__**https://youtu.be/abc?t=69**__||.");
        Check("underscore identifier", "https://youtu.be/abc?si=token__", "https://youtu.be/abc");
        Check("underscore identifier bold", "**https://youtu.be/abc?si=token__**", "**https://youtu.be/abc**");
        Check("underscore identifier underline", "__https://youtu.be/abc?si=token____", "__https://youtu.be/abc__");
        Check("underscore functional value", "__https://youtu.be/abc?si=token&t=69__", "__https://youtu.be/abc?t=69__");
        Check("concatenated markdown", "[one](https://youtu.be/abc?si=one)[two](https://youtu.be/def?si=two)", "[one](https://youtu.be/abc)[two](https://youtu.be/def)");
        Check("concatenated parenthesized", "(https://youtu.be/abc?si=one)(https://youtu.be/def?si=two)", "(https://youtu.be/abc)(https://youtu.be/def)");
        Check("ambiguous comma links unchanged", "https://youtu.be/abc?si=one,https://youtu.be/def?si=two", "https://youtu.be/abc?si=one,https://youtu.be/def?si=two");
        Check("ambiguous semicolon links unchanged", "https://youtu.be/abc?si=one;https://youtu.be/def?si=two", "https://youtu.be/abc?si=one;https://youtu.be/def?si=two");
        Check("angle brackets", "<https://youtu.be/abc?si=x>", "<https://youtu.be/abc>");
        Check("backticks", "`https://youtu.be/abc?si=x`", "`https://youtu.be/abc`");
        Check("punctuation", "(https://youtu.be/abc?si=x).", "(https://youtu.be/abc).");
        Check("timestamp punctuation", "https://youtu.be/abc?si=x&t=69!", "https://youtu.be/abc?t=69!");
        Check("unicode message", "\u3053\u308c https://youtu.be/abc?si=x \uD83C\uDF89", "\u3053\u308c https://youtu.be/abc \uD83C\uDF89");
        Check("html separator first", "https://youtu.be/abc?si=x&amp;t=69", "https://youtu.be/abc?t=69");
        Check("html separator middle", "https://youtube.com/watch?v=abc&amp;si=x&amp;t=69", "https://youtube.com/watch?v=abc&amp;t=69");
        Check("unrelated site", "https://example.com/watch?si=x&t=69", "https://example.com/watch?si=x&t=69");
        Check("spoof suffix", "https://youtube.com.evil.example/watch?si=x", "https://youtube.com.evil.example/watch?si=x");
        Check("spoof prefix", "https://notyoutube.com/watch?si=x", "https://notyoutube.com/watch?si=x");
        Check("spoof short", "https://youtu.be.evil.example/abc?si=x", "https://youtu.be.evil.example/abc?si=x");
        Check("userinfo evil destination", "https://youtube.com@evil.example/watch?si=x", "https://youtube.com@evil.example/watch?si=x");
        Check("userinfo youtube destination", "https://evil.example@youtube.com/watch?si=x", "https://evil.example@youtube.com/watch?si=x");
        Check("nested redirect", "https://example.com/?url=https://youtu.be/abc?si=x&t=9", "https://example.com/?url=https://youtu.be/abc?si=x&t=9");
        Check("embedded query value", "url=https://youtu.be/abc?si=x", "url=https://youtu.be/abc?si=x");
        Check("embedded bare query value", "url=youtu.be/abc?si=x", "url=youtu.be/abc?si=x");
        Check("email", "someone@youtube.com?si=x", "someone@youtube.com?si=x");
        Check("relative path", "/youtube.com/watch?si=x", "/youtube.com/watch?si=x");
        Check("ftp", "ftp://youtube.com/watch?si=x", "ftp://youtube.com/watch?si=x");
        Check("backslash authority", "https://youtube.com\\@evil.example/?si=x", "https://youtube.com\\@evil.example/?si=x");
        Check("lookalike parameter", "https://youtu.be/abc?si2=x&xsi=x&SIgn=x&t=9", "https://youtu.be/abc?si2=x&xsi=x&SIgn=x&t=9");
        Check("double encoded key", "https://youtu.be/abc?%2573%2569=x", "https://youtu.be/abc?%2573%2569=x");
        Check("no tracker", "https://youtube.com/watch?v=abc&t=69&list=PL", "https://youtube.com/watch?v=abc&t=69&list=PL");
        Check("empty", "", "");
        Check("null", null, null);
        Check("plain text", "My SI value: hello & si=xyz", "My SI value: hello & si=xyz");

        Check("X requested post", "https://x.com/coolporyg0n/status/2116327687707123755", "https://fixupx.com/coolporyg0n/status/2116327687707123755");
        Check("X path query and fragment", "https://x.com/person/status/123/video/1?s=20&t=a%2Fb#part", "https://fixupx.com/person/status/123/video/1?s=20&t=a%2Fb#part");
        Check("X query si retained", "https://x.com/person/status/123?si=leave&s=20", "https://fixupx.com/person/status/123?si=leave&s=20");
        Check("X www", "https://www.x.com/person/status/123", "https://fixupx.com/person/status/123");
        Check("X bare", "x.com/person/status/123", "fixupx.com/person/status/123");
        Check("X bare www", "www.x.com/person/status/123", "fixupx.com/person/status/123");
        Check("X bare path with scheme", "x.com/a/https://example.com", "fixupx.com/a/https://example.com");
        Check("X nested same site URL retained", "https://x.com/?url=https://x.com/p/status/1", "https://fixupx.com/?url=https://x.com/p/status/1");
        Check("X case", "HTTPS://X.COM/Person/status/123?Q=%2f", "HTTPS://fixupx.com/Person/status/123?Q=%2f");
        Check("X http", "http://x.com/person/status/123", "http://fixupx.com/person/status/123");
        Check("X port", "https://x.com:443/person/status/123", "https://fixupx.com:443/person/status/123");
        Check("X root dot", "https://x.com./person/status/123", "https://fixupx.com/person/status/123");
        Check("X root", "https://x.com", "https://fixupx.com");
        Check("X root query", "https://x.com?s=20#part", "https://fixupx.com?s=20#part");
        Check("X plain message", "Watch https://x.com/p/status/123 please.", "Watch https://fixupx.com/p/status/123 please.");
        Check("X markdown", "[video](https://x.com/p/status/123)", "[video](https://fixupx.com/p/status/123)");
        Check("X spoiler bold", "||**https://x.com/p/status/123**||.", "||**https://fixupx.com/p/status/123**||.");
        Check("X underline", "__https://x.com/p/status/123__", "__https://fixupx.com/p/status/123__");
        Check("X angle brackets", "<https://x.com/p/status/123>", "<https://fixupx.com/p/status/123>");
        Check("X backticks", "`https://x.com/p/status/123`", "`https://fixupx.com/p/status/123`");
        Check("X unicode message", "\uD83C\uDF89 https://x.com/p/status/123\r\nnext", "\uD83C\uDF89 https://fixupx.com/p/status/123\r\nnext");
        Check("X multiple", "https://x.com/a/status/1 https://x.com/b/status/2", "https://fixupx.com/a/status/1 https://fixupx.com/b/status/2");
        Check("X and YouTube", "https://x.com/a/status/1\nhttps://youtu.be/abc?si=secret&t=69", "https://fixupx.com/a/status/1\nhttps://youtu.be/abc?t=69");
        Check("X concatenated Markdown", "[a](https://x.com/a/status/1)[b](https://x.com/b/status/2)", "[a](https://fixupx.com/a/status/1)[b](https://fixupx.com/b/status/2)");
        Check("X already fixed", "https://fixupx.com/a/status/1", "https://fixupx.com/a/status/1");
        Check("X bare already fixed", "fixupx.com/a/status/1", "fixupx.com/a/status/1");
        Check("X unrelated URL", "https://example.com/x.com/p/status/1", "https://example.com/x.com/p/status/1");
        Check("X nested foreign URL", "https://example.com/?url=https://x.com/p/status/1", "https://example.com/?url=https://x.com/p/status/1");
        Check("X query value", "url=https://x.com/p/status/1", "url=https://x.com/p/status/1");
        Check("X bare query value", "url=x.com/p/status/1", "url=x.com/p/status/1");
        Check("X spoof suffix", "https://x.com.evil.example/p/status/1", "https://x.com.evil.example/p/status/1");
        Check("X spoof prefix", "https://notx.com/p/status/1", "https://notx.com/p/status/1");
        Check("X unrelated subdomain", "https://other.x.com/p/status/1", "https://other.x.com/p/status/1");
        Check("X userinfo evil destination", "https://x.com@evil.example/p/status/1", "https://x.com@evil.example/p/status/1");
        Check("X userinfo X destination", "https://evil.example@x.com/p/status/1", "https://evil.example@x.com/p/status/1");
        Check("X email", "person@x.com", "person@x.com");
        Check("X FTP", "ftp://x.com/p/status/1", "ftp://x.com/p/status/1");
        Check("X scheme relative", "//x.com/p/status/1", "//x.com/p/status/1");
        Check("X relative path", "/x.com/p/status/1", "/x.com/p/status/1");
        Check("X backslash authority", "https://x.com\\@evil.example/p/status/1", "https://x.com\\@evil.example/p/status/1");
        Check("X ambiguous comma links", "https://x.com/a/status/1,https://x.com/b/status/2", "https://x.com/a/status/1,https://x.com/b/status/2");
        Check("X ambiguous mixed links", "https://youtu.be/abc?si=x,x.com/a/status/1", "https://youtu.be/abc?si=x,x.com/a/status/1");
        Console.WriteLine(assertions + " assertions; " + failures + " failures.");
        return failures == 0 ? 0 : 1;
    }
}
