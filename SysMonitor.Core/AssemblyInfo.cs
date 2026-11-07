using System.Runtime.CompilerServices;

// IoRate is internal on purpose, but the sampler (assembly "SysMonitor") uses
// it and the tests exercise it directly.
[assembly: InternalsVisibleTo("SysMonitor")]
[assembly: InternalsVisibleTo("SysMonitor.Tests")]
