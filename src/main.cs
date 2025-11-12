using System.Collections;
using System.Diagnostics;
using System.Text;

class Program
{

  static readonly string[] builtins = ["echo", "cd", "exit", "pwd", "history", "type"];
  static List<string> executables = [];
  static Dictionary<string, string> executablePaths = [];
  static void Main()
  {
    FindExecutables();
    executables = [.. executablePaths.Keys];
    executables.Sort((a, b) => a.Length - b.Length);
    while (true)
    {
      Console.Write("$ ");
      string? line = null;
      StringBuilder inputBuilder = new();

      while (true)
      {
        var keyInfo = Console.ReadKey(intercept: true);

        if (keyInfo.Key == ConsoleKey.Enter)
        {
          Console.WriteLine();
          break;
        }
        else if (keyInfo.Key == ConsoleKey.Tab)
        {
          string prefix = inputBuilder.ToString();
          bool foundCompletion = false;
          foreach (var cmd in executables)
          {
            if (cmd.StartsWith(prefix))
            {
              string completion = string.Concat(cmd.AsSpan(prefix.Length), " ");
              inputBuilder.Append(completion);
              Console.Write(completion);
              foundCompletion = true;
              break;
            }
          }
          if (!foundCompletion)
            Console.Write("\a");
        }
        else
        {
          inputBuilder.Append(keyInfo.KeyChar);
          Console.Write(keyInfo.KeyChar);
        }
      }

      if (inputBuilder.Length > 0)
        line = inputBuilder.ToString();
      if (line == null)
        continue;

      if (line == "exit 0")
        Environment.Exit(0);

      List<string> args = ParseArgs(line);
      var state = ProcessRedirect(args);
      string firstToken = args.First();

      if (builtins.Contains(firstToken))
      {
        var (output, errOut) = RunBuiltins(state.Args);
        WriteOutput(output, errOut, state);
        continue;
      }

      if (!executablePaths.ContainsKey(firstToken))
        Console.WriteLine($"{line}: command not found");
      else
      {
        var startInfo = new ProcessStartInfo { FileName = args.First(), UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var e in state.Args.Skip(1))
        {
          if (!string.IsNullOrEmpty(e) && e != " ")
            startInfo.ArgumentList.Add(e);
        }
        using var process = Process.Start(startInfo);
        string? output = startInfo.RedirectStandardOutput ? process?.StandardOutput.ReadToEnd() : null;
        string? errOutput = startInfo.RedirectStandardError ? process?.StandardError.ReadToEnd() : null;
        process?.WaitForExit();
        WriteOutput(output, errOutput, state);
      }
    }
  }

  static void WriteOutput(string? output, string? errOutput, State state)
  {
    if (state.OutPath != null)
    {
      string content = output ?? "";
      if (state.AppendOut)
        File.AppendAllText(state.OutPath, content);
      else
        File.WriteAllText(state.OutPath, content);
    }
    else
      Console.Write(output);


    if (state.ErrPath != null)
    {
      string content = errOutput ?? "";
      if (state.AppendErr)
        File.AppendAllText(state.ErrPath, content);
      else
        File.WriteAllText(state.ErrPath, content);
    }
    else
      Console.Error.Write(errOutput);
  }

  static State ProcessRedirect(List<string> args)
  {
    HashSet<string> redirectTypes = [">", "1>", ">>", "1>>", "2>", "2>>", "&>"];
    int redirectIdx = args.FindIndex(redirectTypes.Contains);
    if (redirectIdx < 0)
      return new(args);

    string op = args[redirectIdx];
    string path = args[redirectIdx + 1];

    return new(
          Args: args.GetRange(0, redirectIdx),
          OutPath: op.StartsWith("2") ? null : path,
          ErrPath: op.StartsWith("2") || op == "&>" ? path : null,
          AppendOut: op == "1>>" || op == ">>" || op == "&>",
          AppendErr: op == "2>>" || op == "&>"
    );
  }

  static (string?, string?) RunBuiltins(IReadOnlyList<string> args)
  {
    string command = args.First();
    string input = string.Join(" ", args.Skip(1));

    if (command == "echo")
    {
      return (input + '\n', null);
    }

    else if (command == "pwd")
    {
      string dir = Environment.CurrentDirectory;
      if (dir.StartsWith("/private")) // for mac
        dir = dir[8..];
      return (dir + '\n', null);
    }
    else if (command == "cd")
    {
      string dir = input;
      if (dir == "~")
        dir = Environment.GetEnvironmentVariable("HOME")!;

      if (!Path.Exists(dir))
      {
        return (null, $"cd: {dir}: No such file or directory\n");
      }
      Directory.SetCurrentDirectory(dir);
    }
    else if (command == "type")
    {
      return (Type(input) + '\n', null);
    }
    return (null, null);
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

  static string Type(string cmd)
  {
    if (executablePaths.TryGetValue(cmd, out string? value))
      return $"{cmd} is {value}";
    else
      return $"{cmd}: not found";
  }

  static void FindExecutables()
  {
    foreach (var cmd in builtins)
      executablePaths[cmd] = "a shell builtin";

    string? paths = Environment.GetEnvironmentVariable("PATH");
    foreach (var dir in paths!.Split(":"))
    {
      if (Directory.Exists(dir))
      {
        foreach (var file in Directory.GetFileSystemEntries(dir))
        {
          if (executablePaths.ContainsKey(Path.GetFileName(file)))
            continue;
          var path = Path.Combine(dir, file);
          FileInfo info = new(path);
          if (info.ResolveLinkTarget(true) != null)
            path = info.ResolveLinkTarget(true)?.FullName;

          if (File.Exists(path) && IsExecutable(path))
            executablePaths[Path.GetFileName(file)] = path;
        }
      }
    }
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

public record State(
  IReadOnlyList<string> Args,
  string? OutPath = null,
  string? ErrPath = null,
  bool AppendOut = false,
  bool AppendErr = false
);
