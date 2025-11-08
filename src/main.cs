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

      List<string> args = ParseArgs(line);
      string firstToken = args.First();

      if (firstToken == "echo")
      {
        Console.WriteLine(string.Join(" ", args.Skip(1)));
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
          var startInfo = new ProcessStartInfo { FileName = args.First(), UseShellExecute = false };
          foreach (var e in args.Skip(1))
          {
            if (!string.IsNullOrEmpty(e) && e != " ")
              startInfo.ArgumentList.Add(e);
          }

          using var process = Process.Start(startInfo);
          // using var process = Process.Start(new ProcessStartInfo { FileName = firstToken, Arguments = line[firstToken.Length..] });
          // using var process = Process.Start(new ProcessStartInfo { FileName = "/bin/sh", Arguments = $"-c \"{line}\"", UseShellExecute = false });
          process?.WaitForExit();
        }
      }

    }
  }

  static List<string> ParseArgs(string line)
  {
    List<string> args = [];
    StringBuilder sb = new();
    bool inSingleQuote = false;
    bool inDoubleQuote = false;
    for (int i = 0; i < line.Length; ++i)
    {
      char c = line[i];
      if (c == '\\' && !inSingleQuote)
      {
        if (i + 1 < line.Length)
        {
          char next = line[i + 1];
          if (inDoubleQuote)
          {
            if (next == '"' || next == '\\' || next == '$' || next == '`')
            {
              sb.Append(next);
              i++;
              continue;
            }
            sb.Append(c);
          }
          else
          {
            sb.Append(next);
            i++;
          }
        }
        continue;
      }

      if (c == '\'' && !inDoubleQuote)
      {
        inSingleQuote = !inSingleQuote;
        continue;
      }

      if (c == '"' && !inSingleQuote)
      {
        inDoubleQuote = !inDoubleQuote;
        continue;
      }

      if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
      {
        if (sb.Length > 0)
        {
          args.Add(sb.ToString());
          sb.Clear();
        }
        continue;
      }
      sb.Append(c);
    }

    if (sb.Length > 0)
      args.Add(sb.ToString());
    return args;
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

