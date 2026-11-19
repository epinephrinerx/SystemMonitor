using System.Runtime.CompilerServices;

// The parsers are the parts worth testing, and the test project lives in its
// own assembly. Nothing outside the Linux build should call the helpers.
[assembly: InternalsVisibleTo("SysMonitor.Linux.Tests")]
