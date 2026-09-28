using System.Runtime.CompilerServices;

// The descriptor parsing and the I/O rate maths are internal on purpose --
// nothing outside the sensor layer should call them -- but they are exactly
// the parts worth testing, so the test assembly gets in.
[assembly: InternalsVisibleTo("SysMonitor.Tests")]
