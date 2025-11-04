using System.Collections;
using System.Diagnostics;
using System.Text;

class Program
{

  static readonly string[] builtins = ["echo", "cd", "exit", "pwd", "history", "type"];
  List<string> executables = [.. builtins];
  static void Main()
  {
    while (true)
    {
      Console.Write("$ ");
      string? line = Console.ReadLine();
      if (line == null)
        continue;

      if (line == "exit 0")
        Environment.Exit(0);

      int firstTokenIdx = line.IndexOf(' ');
      string firstToken = firstTokenIdx < 0 ? line : line[0..firstTokenIdx];

      if (firstToken == "echo")
      {
        StringBuilder sb = new();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        for (int i = 5; i < line.Length; ++i)
        {
          if (line[i] == '\\')
          {
            if (inSingleQuote)
              sb.Append('\\');

            sb.Append(line[++i]);
            continue;
          }

          while (i + 2 < line.Length && line[i] == ' ' && line[i + 1] == ' ')
          {
            if (inSingleQuote || inDoubleQuote)
              sb.Append(line[i]);
            i++;
          }

          if (!inDoubleQuote && line[i] == '\'')
          {
            inSingleQuote = !inSingleQuote;
            continue;
          }
          if (line[i] == '"')
          {
            inDoubleQuote = !inDoubleQuote;
            continue;
          }
          sb.Append(line[i]);
        }

        Console.WriteLine(sb.ToString());
      }

      else if (firstToken == "pwd")
      {
        string dir = Environment.CurrentDirectory;
        if (dir.StartsWith("/private")) // for mac
          dir = dir[8..];
        Console.WriteLine(dir);
      }
      else if (firstToken == "cd")
      {
        string dir = line[3..];
        if (dir == "~")
          dir = Environment.GetEnvironmentVariable("HOME")!;

        if (!Path.Exists(dir))
        {
          Console.Error.WriteLine($"cd: {dir}: No such file or directory");
          continue;
        }
        Directory.SetCurrentDirectory(dir);
      }
      else if (firstToken == "type")
      {
        Type(line);
      }
      else
      {
        var exe = FindExecutable(firstToken);
        if (exe == null)
          Console.WriteLine($"{line}: command not found");
        else
        {
          if (line.IndexOf('"') == -1)
            line = line.Replace('\'', '"');
          using var process = Process.Start(new ProcessStartInfo { FileName = firstToken, Arguments = line[firstToken.Length..] });
          // using var process = Process.Start(new ProcessStartInfo { FileName = "/bin/sh", Arguments = $"-c \"{line}\"", UseShellExecute = false });
          process?.WaitForExit();
        }
      }

    }
  }

  static void Type(string line)
  {
    string secondToken = line[5..];
    if (builtins.Contains(secondToken))
      Console.WriteLine($"{secondToken} is a shell builtin");
    else
    {
      var path = FindExecutable(secondToken);
      if (path == null)
        Console.WriteLine($"{secondToken}: not found");
      else
        Console.WriteLine($"{secondToken} is {path}");
    }
  }

  static string? FindExecutable(string program)
  {
    string? path = Environment.GetEnvironmentVariable("PATH");
    foreach (var dir in path!.Split(":"))
    {
      var fullPath = Path.Combine(dir, program);
      if (File.Exists(fullPath) && IsExecutable(fullPath))
        return fullPath;
    }
    return null;
  }

  static bool IsExecutable(string path)
  {
    if (OperatingSystem.IsWindows())
    {
      string ext = Path.GetExtension(path).ToLowerInvariant();
      return ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".com";
    }

    UnixFileMode mode = File.GetUnixFileMode(path);
    return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
  }

}

