using System.IO;

namespace Drilling.Common.Log;

public static class CProgramOpenLog
{
    private static string LogDirectory => Path.Combine(
        FindProjectRoot(),
        "Log",
        "ProgramOpen");

    public static string LogPath => Path.Combine(
        LogDirectory,
        $"ProgramOpen_{DateTime.Now:yyyyMMdd}.log");

    public static void Write(
        string title,
        Exception exception)
    {
        Write(title, exception.ToString());
    }

    public static void Write(
        string title,
        string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            System.IO.File.AppendAllText(
                LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{title}]{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Startup logging must never become another startup failure.
        }
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "Drilling.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, "Config")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
