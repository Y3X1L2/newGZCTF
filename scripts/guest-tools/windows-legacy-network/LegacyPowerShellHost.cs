using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

// Fixed local PowerShell engine host for QGA's byte streams. It does not load
// a user's profile or the legacy graphical ConsoleHost, or change any policy.
public static class LegacyPowerShellHost
{
    public static int Main()
    {
        Stream stdout = Console.OpenStandardOutput(), stderr = Console.OpenStandardError();
        try
        {
            string source;
            using (MemoryStream memory = new MemoryStream())
            {
                Stream stdin = Console.OpenStandardInput(); byte[] bytes = new byte[4096]; int count;
                while ((count = stdin.Read(bytes, 0, bytes.Length)) != 0)
                {
                    if (memory.Length + count > 65536) throw new InvalidOperationException("Input exceeds 64 KiB");
                    memory.Write(bytes, 0, count);
                }
                source = new UTF8Encoding(false, true).GetString(memory.ToArray());
            }
            if (source.Length == 0)
            {
                byte[] marker = Encoding.UTF8.GetBytes("GZCTF_GUEST_STDIN_UNAVAILABLE");
                stderr.Write(marker, 0, marker.Length); stderr.Flush(); return 78;
            }
            using (Runspace runspace = RunspaceFactory.CreateRunspace())
            {
                runspace.Open();
                using (Pipeline pipeline = runspace.CreatePipeline())
                {
                    pipeline.Commands.AddScript(source);
                    Collection<PSObject> values = pipeline.Invoke();
                    foreach (PSObject value in values)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(value.ToString() + Environment.NewLine);
                        stdout.Write(bytes, 0, bytes.Length);
                    }
                    Collection<object> errors = pipeline.Error.ReadToEnd();
                    if (errors.Count != 0)
                    {
                        foreach (object error in errors)
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(error.ToString() + Environment.NewLine);
                            stderr.Write(bytes, 0, bytes.Length);
                        }
                        stderr.Flush(); return 1;
                    }
                }
            }
            stdout.Flush(); return 0;
        }
        catch (Exception exception)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(exception.GetType().Name + ": " + exception.Message);
            stderr.Write(bytes, 0, bytes.Length); stderr.Flush(); return 1;
        }
    }
}
