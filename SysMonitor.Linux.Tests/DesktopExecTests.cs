using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class DesktopExecTests
{
    [TestMethod]
    public void ParseExec_SimpleCommandWithFieldCode_RemovesFieldCode()
    {
        IReadOnlyList<string>? argv = DesktopEntry.ParseExec("firefox %u");

        Assert.IsNotNull(argv);
        Assert.AreEqual(1, argv.Count);
        Assert.AreEqual("firefox", argv[0]);
    }

    [TestMethod]
    public void ParseExec_QuotedExecutableWithSpaces_ReturnsTwoArguments()
    {
        IReadOnlyList<string>? argv = DesktopEntry.ParseExec("\"/opt/My App/run\" --x");

        Assert.IsNotNull(argv);
        Assert.AreEqual(2, argv.Count);
        Assert.AreEqual("/opt/My App/run", argv[0]);
        Assert.AreEqual("--x", argv[1]);
    }

    [TestMethod]
    public void ParseExec_EscapedQuoteInsideQuotes_PreservesQuote()
    {
        IReadOnlyList<string>? argv = DesktopEntry.ParseExec("\"echo \\\"hello world\\\"\"");

        Assert.IsNotNull(argv);
        Assert.AreEqual(1, argv.Count);
        Assert.AreEqual("echo \"hello world\"", argv[0]);
    }

    [TestMethod]
    public void ParseExec_DoublePercent_BecomesSinglePercent()
    {
        IReadOnlyList<string>? argv = DesktopEntry.ParseExec("myapp %% --ratio 50%%");

        Assert.IsNotNull(argv);
        Assert.AreEqual(4, argv.Count);
        Assert.AreEqual("myapp", argv[0]);
        Assert.AreEqual("%", argv[1]);
        Assert.AreEqual("--ratio", argv[2]);
        Assert.AreEqual("50%", argv[3]);
    }

    [TestMethod]
    public void ParseExec_SpecFieldCodes_AreRemoved()
    {
        // Spec field codes: %F %i %c %k (as well as %f %u %U %d %D %n %N %v %m)
        IReadOnlyList<string>? argv = DesktopEntry.ParseExec("editor %F %i %c %k --flag");

        Assert.IsNotNull(argv);
        Assert.AreEqual(2, argv.Count);
        Assert.AreEqual("editor", argv[0]);
        Assert.AreEqual("--flag", argv[1]);
    }

    [TestMethod]
    public void ParseExec_UnbalancedQuotes_ReturnsNull()
    {
        Assert.IsNull(DesktopEntry.ParseExec("\"/opt/My App/run --x"));
        Assert.IsNull(DesktopEntry.ParseExec("editor \"unterminated"));
    }

    [TestMethod]
    public void ParseExec_EmptyOrWhitespace_ReturnsNull()
    {
        Assert.IsNull(DesktopEntry.ParseExec(null));
        Assert.IsNull(DesktopEntry.ParseExec(""));
        Assert.IsNull(DesktopEntry.ParseExec("   \t  "));
    }

    [TestMethod]
    public void ParseExec_StringValueBackslash_Unescapes()
    {
        // When desktop entry value has \\, it represents a single backslash
        IReadOnlyList<string>? argv = DesktopEntry.ParseExec("app \\\\arg");

        Assert.IsNotNull(argv);
        Assert.AreEqual(2, argv.Count);
        Assert.AreEqual("app", argv[0]);
        Assert.AreEqual("\\arg", argv[1]);
    }

    [TestMethod]
    public void UnescapeValue_DecodesStandardEscapeSequences()
    {
        string raw = @"My\sApplication\nSecond\tLine\\Backslash";
        string unescaped = DesktopEntry.UnescapeValue(raw);

        Assert.AreEqual("My Application\nSecond\tLine\\Backslash", unescaped);
    }

    [TestMethod]
    public void ParseExec_MisplacedQuotes_ReturnsNull()
    {
        // Quotes starting mid-argument or text immediately following closed quote
        Assert.IsNull(DesktopEntry.ParseExec("\"foo\"bar"));
        Assert.IsNull(DesktopEntry.ParseExec("foo\"bar\""));
        Assert.IsNull(DesktopEntry.ParseExec("/bin/echo \"foo\"bar"));
        Assert.IsNull(DesktopEntry.ParseExec("/bin/echo foo\"bar\""));
        Assert.IsNull(DesktopEntry.ParseExec("\"a\"\"b\""));
    }
}
