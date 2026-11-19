using System.Runtime.CompilerServices;

// Types such as IoRate are internal on purpose -- nothing outside the sensor
// layer should call them -- but they are exactly the parts worth testing, so
// the Linux build and its test project can see them.
[assembly: InternalsVisibleTo("SysMonitor.Linux")]
[assembly: InternalsVisibleTo("SysMonitor.Linux.Tests")]
