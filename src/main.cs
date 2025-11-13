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
      string? line = ReadInputWithCompletion();

      if (line == null)
        continue;

      if (line == "exit 0")
        Environment.Exit(0);

      List<string> args = ParseArgs(line);
      var state = ProcessRedirect(args);

      if (builtins.Contains(args.First()))
      {
        var (output, err) = RunBuiltins(state.Args);
        WriteOutput(output, err, state);
      }
      else if (executablePaths.ContainsKey(args.First()))
      {
        var (output, err) = RunExternal(state.Args);
        WriteOutput(output, err, state);
      }
      else
        Console.WriteLine($"{args.First()}: command not found");
    }
  }

  static string? ReadInputWithCompletion()
  {
    StringBuilder sb = new();
    bool showMultiple = false;
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
        string prefix = sb.ToString();
        var candidates = executables.Where(cmd => cmd.StartsWith(prefix)).ToArray();

        if (candidates.Length == 0)
        {
          Console.Write("\a");
          continue;
        }

        var multi = candidates.Where(cmd => cmd.Length == candidates.First().Length).ToList();
        if (multi.Count > 1)
        {
          if (showMultiple)
          {
            Console.WriteLine();
            multi.Sort();
            string comp = string.Join("  ", multi);
            string completion = comp;
            Console.WriteLine(completion);
            Console.Write($"$ {sb}");
            showMultiple = false;
          }
          else
            Console.Write("\a");
          showMultiple = true;
        }
        else
        {
          string trail = candidates.Length > 1 ? "" : " ";
          string completion = string.Concat(candidates.First().AsSpan(prefix.Length), trail);
          sb.Append(completion);
          Console.Write(completion);
        }

      }
      else
      {
        sb.Append(keyInfo.KeyChar);
        Console.Write(keyInfo.KeyChar);
      }
    }

    return sb.Length > 0 ? sb.ToString() : null;
  }

  static void WriteOutput(string? output, string? err, State state)
  {
    if (state.OutPath != null)
      WriteToFile(output ?? "", state.OutPath, state.AppendOut);
    else
      Console.Write(output);

    if (state.ErrPath != null)
      WriteToFile(err ?? "", state.ErrPath, state.AppendErr);
    else
      Console.Error.Write(err);
  }

  static void WriteToFile(string content, string path, bool append)
  {
    if (append) File.AppendAllText(path, content);
    else File.WriteAllText(path, content);
  }

  static State ProcessRedirect(List<string> args)
  {
    int redirectIdx = args.FindIndex(a => a is ">" or "1>" or ">>" or "1>>" or "2>" or "2>>" or "&>");
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

  static (string? output, string? err) RunExternal(IReadOnlyList<string> args)
  {
    var startInfo = new ProcessStartInfo { FileName = args.First(), UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
    foreach (var e in args.Skip(1).Where(a => !string.IsNullOrWhiteSpace(a)))
      startInfo.ArgumentList.Add(e);

    using var process = Process.Start(startInfo);
    string? output = process?.StandardOutput.ReadToEnd();
    string? err = process?.StandardError.ReadToEnd();
    process?.WaitForExit();
    return (output, err);
  }

  static (string?, string?) RunBuiltins(IReadOnlyList<string> args)
  {
    string command = args[0];
    string input = string.Join(" ", args.Skip(1));

    if (command == "echo")
      return (input + '\n', null);
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
        return (null, $"cd: {dir}: No such file or directory\n");
      Directory.SetCurrentDirectory(dir);
    }
    else if (command == "type")
      return (Type(input) + '\n', null);

    return (null, null);
  }

  static List<string> ParseArgs(string line)
  {
    List<string> args = [];
    StringBuilder sb = new();
    bool inSingleQuote = false, inDoubleQuote = false;
    for (int i = 0; i < line.Length; ++i)
    {
      char c = line[i];
      if (c == '\\' && !inSingleQuote)
      {
        if (i + 1 < line.Length)
        {
          char next = line[++i];
          if (!inDoubleQuote || next == '"' || next == '\\' || next == '$' || next == '`')
          {
            sb.Append(next);
            continue;
          }
          sb.Append('\\').Append(next);
        }
      }
      else if (c == '\'' && !inDoubleQuote)
        inSingleQuote = !inSingleQuote;
      else if (c == '"' && !inSingleQuote)
        inDoubleQuote = !inDoubleQuote;
      else if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
      {
        if (sb.Length > 0)
        {
          args.Add(sb.ToString());
          sb.Clear();
        }
      }
      else
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

    var paths = Environment.GetEnvironmentVariable("PATH")?.Split(':') ?? [];
    foreach (var dir in paths.Where(Directory.Exists))
    {
      foreach (var file in Directory.GetFileSystemEntries(dir))
      {
        string name = Path.GetFileName(file);
        if (executablePaths.ContainsKey(name)) continue;

        var path = new FileInfo(file).ResolveLinkTarget(true)?.FullName ?? file;
        if (File.Exists(path) && IsExecutable(path))
          executablePaths[name] = file;
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
