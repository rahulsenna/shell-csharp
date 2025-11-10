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

      if (builtins.Contains(firstToken))
      {

        if (GetRedirectPath(args) is var (redirectPath, rIdx, rType) && redirectPath != null)
        {
          string output = RunBuiltins(firstToken, string.Join(" ", args[1..rIdx])) + '\n';
            File.WriteAllText(redirectPath, output);
        }
        else
        {
          string output = RunBuiltins(firstToken, string.Join(" ", args.Skip(1)));
          if (!string.IsNullOrEmpty(output))
            Console.WriteLine(output);
        }          
        continue;
      }

      var exe = FindExecutable(firstToken);
      if (exe == null)
        Console.WriteLine($"{line}: command not found");
      else
      {
        var startInfo = new ProcessStartInfo { FileName = args.First(), UseShellExecute = false };

        string? redirectPath = null;
        foreach (var e in args.Skip(1))
        {
          if (e == ">" || e == "1>")
          {
            startInfo.RedirectStandardOutput = true;
            continue;
          }
          
          if (startInfo.RedirectStandardOutput || startInfo.RedirectStandardError)
          {
            redirectPath = e;
            continue;
          }

          if (!string.IsNullOrEmpty(e) && e != " ")
            startInfo.ArgumentList.Add(e);
        }

        using var process = Process.Start(startInfo);
        if (redirectPath != null)
        {
          var output = process?.StandardOutput.ReadToEnd();
          File.WriteAllText(redirectPath, output);
        }
        // using var process = Process.Start(new ProcessStartInfo { FileName = firstToken, Arguments = line[firstToken.Length..] });
        // using var process = Process.Start(new ProcessStartInfo { FileName = "/bin/sh", Arguments = $"-c \"{line}\"", UseShellExecute = false });
        process?.WaitForExit();
      }


    }
  }

  static (string?, int, string?) GetRedirectPath(List<string> args)
  {
    string[] redirectTypes = [">", "1>", "2>", ">>"];

    foreach (var rType in redirectTypes)
    {
      int redirectIdx = args.IndexOf(rType);
      if (redirectIdx > 0)
      {
        return (args[redirectIdx + 1], redirectIdx, rType);
      }
    }

    return (null, -1, null);
  }

  static string RunBuiltins(string command, string input)
  {
    if (command == "echo")
    {
      return input;
    }

    else if (command == "pwd")
    {
      string dir = Environment.CurrentDirectory;
      if (dir.StartsWith("/private")) // for mac
        dir = dir[8..];
      return dir;
    }
    else if (command == "cd")
    {
      string dir = input;
      if (dir == "~")
        dir = Environment.GetEnvironmentVariable("HOME")!;

      if (!Path.Exists(dir))
      {
        return $"cd: {dir}: No such file or directory";
      }
      Directory.SetCurrentDirectory(dir);
      return "";
    }
    else if (command == "type")
    {
      return Type(input);
    }
    return "";
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

  static string Type(string line)
  {
    if (builtins.Contains(line))
      return $"{line} is a shell builtin";
    else
    {
      var path = FindExecutable(line);
      if (path == null)
        return $"{line}: not found";
      else
        return $"{line} is {path}";
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

