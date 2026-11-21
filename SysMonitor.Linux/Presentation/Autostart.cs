using System.Text;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The XDG autostart entry (spec section 9, Linux decision). One
/// <c>.desktop</c> file under <c>$XDG_CONFIG_HOME/autostart</c> is what the
/// desktop session starts, so the whole feature is one small text file.
///
/// Both the text and its reading are pure functions: the probe can show what
/// would be written without writing it, and a test can feed any file through
/// the same rules.
///
/// The Exec value is escaped in two stages (Amendment 2, item 3):
/// <list type="number">
/// <item>Exec quoting: inside <c>"…"</c>, <c>"</c>, <c>`</c>, <c>$</c> and
/// <c>\</c> are prefixed with <c>\</c>, and <c>%</c> becomes <c>%%</c>.</item>
/// <item>String-value escaping: every <c>\</c> is doubled, because the value
/// is a string in a key file.</item>
/// </list>
/// So a literal <c>$</c> reaches the file as <c>\\$</c> and a literal
/// <c>\</c> as <c>\\\\</c>. <see cref="Unquote"/> reverses both stages.
/// </summary>
public static class DesktopEntry
{
    public const string FileName = "systemmonitor.desktop";

    /// <summary>The file the app writes when autostart is switched on.</summary>
    public static string Build(string executablePath) =>
        string.Join('\n', new[]
        {
            "[Desktop Entry]",
            "Type=Application",
            "Name=System Monitor",
            "Exec=" + Quote(executablePath),
            "Icon=systemmonitor",
            "X-GNOME-Autostart-enabled=true",
            "Terminal=false",
        }) + "\n";

    /// <summary>
    /// The Exec value exactly as it goes into the file: both escaping stages
    /// applied. It is what makes <c>Exec=/usr/bin/a\$b</c> read back correctly.
    /// </summary>
    public static string Quote(string value) => EscapeStringValue(QuoteExec(value));

    /// <summary>
    /// Stage one: the Desktop Entry spec's Exec quoting. A quoted argument
    /// escapes <c>"</c>, <c>`</c>, <c>$</c> and <c>\</c> with a backslash, and
    /// <c>%</c> is doubled because the spec reserves it for field codes.
    /// </summary>
    public static string QuoteExec(string value)
    {
        var quoted = new StringBuilder(value.Length + 2);
        quoted.Append('"');
        foreach (char character in value)
        {
            if (character is '"' or '`' or '$' or '\\')
            {
                quoted.Append('\\');
            }
            else if (character == '%')
            {
                quoted.Append('%');
            }
            quoted.Append(character);
        }
        quoted.Append('"');
        return quoted.ToString();
    }

    /// <summary>Stage two: a string value doubles every backslash.</summary>
    public static string EscapeStringValue(string value) => value.Replace("\\", "\\\\");

    /// <summary>
    /// The single argument an Exec value holds, with both escaping stages
    /// removed and field codes reversed (<c>%%</c> to <c>%</c>). Null means
    /// the value is empty or its quotes never close; both are a file the app
    /// did not write and cannot trust.
    /// </summary>
    public static string? Unquote(string? exec)
    {
        if (exec is null)
        {
            return null;
        }

        string text = UnescapeStringValue(exec.Trim());
        if (text.Length == 0 || !TryToken(text, out string value, out _))
        {
            return null;
        }
        return CollapseFieldCodes(value);
    }

    /// <summary>
    /// Amendment 2, item 4: an Exec value that holds exactly one argument,
    /// either one quoted token with only whitespace after its closing quote or
    /// one unquoted token with no whitespace inside it. Null means there is an
    /// extra argument, which means the entry does not start this program.
    /// </summary>
    public static string? SingleArgument(string? exec)
    {
        if (exec is null)
        {
            return null;
        }

        string text = UnescapeStringValue(exec.Trim());
        if (text.Length == 0 || !TryToken(text, out string value, out int end))
        {
            return null;
        }
        if (text[end..].Trim().Length > 0)
        {
            return null;
        }
        return CollapseFieldCodes(value);
    }

    /// <summary>
    /// True when the value can be held by a desktop entry. A newline, CR, tab
    /// or any other control character cannot be escaped in the Exec key, so
    /// autostart cannot be enabled for such a path (Amendment 2, item 3).
    /// </summary>
    public static bool CanRepresent(string? value)
    {
        if (value is null)
        {
            return false;
        }
        foreach (char character in value)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The spec's three conditions: the file exists (the caller checks that),
    /// it is not <c>Hidden=true</c>, and its Exec holds exactly this
    /// executable and nothing else. A file that points at an older install, or
    /// one that adds an argument, is not "enabled".
    /// </summary>
    public static bool IsEnabled(string? content, string executablePath)
    {
        if (content is null)
        {
            return false;
        }

        bool hidden = false;
        string? exec = null;
        foreach (string raw in content.Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] is '#' or '[')
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            if (string.Equals(key, "Hidden", StringComparison.Ordinal))
            {
                hidden = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
            else if (string.Equals(key, "Exec", StringComparison.Ordinal) && exec is null)
            {
                exec = value;
            }
        }

        if (hidden || exec is null)
        {
            return false;
        }

        return string.Equals(SingleArgument(exec), executablePath, StringComparison.Ordinal);
    }

    /// <summary>
    /// One token from an Exec value whose string-value escaping is already
    /// reversed. <paramref name="end"/> is the index just past it, so a caller
    /// can see whether another argument follows.
    /// </summary>
    private static bool TryToken(string text, out string value, out int end)
    {
        value = string.Empty;
        end = 0;
        if (text.Length == 0)
        {
            return false;
        }

        if (text[0] != '"')
        {
            int space = text.IndexOfAny(new[] { ' ', '\t' });
            int stop = space < 0 ? text.Length : space;
            if (stop == 0)
            {
                return false;
            }
            value = Unescape(text[..stop]);
            end = stop;
            return true;
        }

        var argument = new StringBuilder(text.Length);
        for (int i = 1; i < text.Length; i++)
        {
            char character = text[i];
            if (character == '\\' && i + 1 < text.Length)
            {
                argument.Append(text[++i]);
                continue;
            }
            if (character == '"')
            {
                value = argument.ToString();
                end = i + 1;
                return true;
            }
            argument.Append(character);
        }
        return false;
    }

    /// <summary>The stage-two inverse: each <c>\\</c> pair is one backslash.</summary>
    private static string UnescapeStringValue(string text)
    {
        if (text.IndexOf('\\') < 0)
        {
            return text;
        }

        var value = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                value.Append('\\');
                i++;
                continue;
            }
            value.Append(text[i]);
        }
        return value.ToString();
    }

    /// <summary><c>%%</c> is the spec's way to write a literal percent sign.</summary>
    private static string CollapseFieldCodes(string text) =>
        text.IndexOf('%') < 0 ? text : text.Replace("%%", "%");

    /// <summary>
    /// Parse an <c>Exec</c> value into argv, following the Desktop Entry spec:
    /// double-quoted arguments, the string-value escapes, then field codes.
    /// A quote may only open an argument, and a closing quote must be followed
    /// by whitespace or the end of the value (Amendment 2 item 6), so
    /// <c>/bin/echo "foo"bar</c> and <c>/bin/echo foo"bar"</c> are rejected.
    /// <c>%f %F %u %U %i %c %k</c> are removed and <c>%%</c> becomes a literal
    /// percent sign; the deprecated <c>%d %D %n %N %v %m</c> are removed
    /// silently. Null for unbalanced quotes, a misplaced quote, or an empty
    /// argument list.
    /// </summary>
    public static IReadOnlyList<string>? ParseExec(string? exec)
    {
        if (string.IsNullOrWhiteSpace(exec))
        {
            return null;
        }

        var arguments = new List<string>();
        var current = new StringBuilder(exec.Length);
        bool quoted = false;
        bool argumentStarted = false;
        bool quotedSegmentClosed = false;

        for (int i = 0; i < exec.Length; i++)
        {
            char character = exec[i];
            if (quoted)
            {
                if (character == '"')
                {
                    quoted = false;
                    quotedSegmentClosed = true;
                }
                else if (character == '\\' && i + 1 < exec.Length)
                {
                    AppendEscape(current, exec[++i]);
                }
                else
                {
                    current.Append(character);
                }
                continue;
            }

            if (character == '"')
            {
                // The spec allows a quote only where an argument begins. A
                // quote in the middle of "foo"bar, or a second segment after a
                // closed quote ("a""b"), is not a Desktop Entry Exec value.
                if (argumentStarted || quotedSegmentClosed)
                {
                    return null;
                }

                quoted = true;
                argumentStarted = true;
            }
            else if (character is ' ' or '\t')
            {
                if (argumentStarted)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    argumentStarted = false;
                    quotedSegmentClosed = false;
                }
            }
            else if (quotedSegmentClosed)
            {
                // Anything but whitespace right after the closing quote makes
                // the value ambiguous; the spec does not allow it.
                return null;
            }
            else if (character == '\\' && i + 1 < exec.Length)
            {
                AppendEscape(current, exec[++i]);
                argumentStarted = true;
            }
            else
            {
                current.Append(character);
                argumentStarted = true;
            }
        }

        if (quoted)
        {
            return null;
        }

        if (argumentStarted)
        {
            arguments.Add(current.ToString());
        }

        var result = new List<string>(arguments.Count);
        foreach (string argument in arguments)
        {
            string cleaned = RemoveFieldCodes(argument);
            if (cleaned.Length > 0)
            {
                result.Add(cleaned);
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Decode a Desktop Entry string value: <c>\s</c>, <c>\n</c>, <c>\t</c>,
    /// <c>\r</c> and <c>\\</c>, leaving anything else as it was written.
    /// </summary>
    public static string UnescapeValue(string value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                result.Append(EscapeChar(value[++i]));
                continue;
            }
            result.Append(value[i]);
        }
        return result.ToString();
    }

    private static void AppendEscape(StringBuilder builder, char escaped) =>
        builder.Append(EscapeChar(escaped));

    private static char EscapeChar(char escaped) => escaped switch
    {
        's' => ' ',
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        _ => escaped,
    };

    private static string RemoveFieldCodes(string argument)
    {
        if (argument.IndexOf('%') < 0)
        {
            return argument;
        }

        var result = new StringBuilder(argument.Length);
        for (int i = 0; i < argument.Length; i++)
        {
            if (argument[i] != '%' || i + 1 >= argument.Length)
            {
                result.Append(argument[i]);
                continue;
            }

            char code = argument[i + 1];
            if (code == '%')
            {
                result.Append('%');
                i++;
            }
            else if ("fFuUickdDnNvm".IndexOf(code) >= 0)
            {
                // A field code, not text. %i is an icon name and is dropped
                // with the rest; this parser never invents an argument for it.
                i++;
            }
            else
            {
                result.Append(argument[i]);
            }
        }
        return result.ToString();
    }

    /// <summary>A backslash removes any special meaning from the next character.</summary>
    private static string Unescape(string text)
    {
        if (text.IndexOf('\\') < 0)
        {
            return text;
        }

        var value = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                value.Append(text[++i]);
                continue;
            }
            value.Append(text[i]);
        }
        return value.ToString();
    }
}

/// <summary>
/// The real <see cref="IAutostart"/>: the one file under the XDG autostart
/// directory. Disabling deletes that file and nothing else; enabling writes a
/// temp file and renames it over the target, so a crash cannot leave a
/// half-written entry behind.
/// </summary>
public sealed class FileAutostart : IAutostart
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private readonly string _executable;
    private readonly Action<string>? _log;

    /// <param name="executable">The running program; <see cref="Environment.ProcessPath"/> by default.</param>
    /// <param name="directory">The autostart directory; the XDG one by default. Tests pass a fake root.</param>
    public FileAutostart(string? executable = null, string? directory = null, Action<string>? log = null)
    {
        _executable = string.IsNullOrWhiteSpace(executable)
            ? Environment.ProcessPath ?? "sysmonitor"
            : executable!;
        _path = System.IO.Path.Combine(directory ?? DefaultDirectory, DesktopEntry.FileName);
        _log = log;
    }

    /// <summary><c>~/.config/autostart/systemmonitor.desktop</c> by default.</summary>
    public string Path => _path;

    public string Executable => _executable;

    /// <summary>False when the path holds a character the Exec key cannot carry.</summary>
    public bool CanEnable => DesktopEntry.CanRepresent(_executable);

    /// <summary>The exact text <see cref="Set"/>(true) would write.</summary>
    public string Text => DesktopEntry.Build(_executable);

    public static string DefaultDirectory
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
            {
                return System.IO.Path.Combine(xdg, "autostart");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            }
            return System.IO.Path.Combine(home, ".config", "autostart");
        }
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return false;
                }
                return DesktopEntry.IsEnabled(File.ReadAllText(_path), _executable);
            }
            catch (Exception exception)
            {
                _log?.Invoke("autostart could not be read: " + exception.Message);
                return false;
            }
        }
    }

    public void Set(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
                _log?.Invoke("autostart disabled: " + _path);
                return;
            }

            // Amendment 2, item 3: a path with a newline or another control
            // character cannot be written as an Exec value, so the setting
            // stays off and the reason goes to Diagnostics.
            if (!CanEnable)
            {
                _log?.Invoke(
                    "autostart cannot be enabled: the executable path contains "
                    + "a character a desktop entry cannot hold");
                return;
            }

            string? directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = _path + ".tmp";
            File.WriteAllText(temp, Text, Utf8);
            File.Move(temp, _path, overwrite: true);
            _log?.Invoke("autostart enabled: " + _path);
        }
        catch (Exception exception)
        {
            _log?.Invoke("autostart could not be written: " + exception.Message);
            try
            {
                string temp = _path + ".tmp";
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception)
            {
                // Best effort; the temp name is predictable and harmless.
            }
        }
    }
}
