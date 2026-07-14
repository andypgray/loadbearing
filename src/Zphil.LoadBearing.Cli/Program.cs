using System.CommandLine;
using System.Text;
using Zphil.LoadBearing.Cli;

// Prose fragments carry em-dashes and backticks; force UTF-8 so a real console renders them (the
// in-process tests bypass Console entirely). Guarded: the setter can throw when stdout is redirected.
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // Redirected or unsupported handle — leave the console's default encoding in place.
}

return await CliEntry.InvokeAsync(args, new InvocationConfiguration());