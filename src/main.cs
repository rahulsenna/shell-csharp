using System.Collections;
using System.Diagnostics;
using System.Text;

class Program
{

  static readonly string[] builtins = ["echo", "cd", "exit", "pwd", "history", "type"];
  static List<string> executables = [];
  static List<string> history = [];
  static int historyIdx = 0;
  static int historyWriteIdx = 0;
  static Dictionary<string, string> executablePaths = [];
  static void Main()
  {
    string? historyFilePath = Environment.GetEnvironmentVariable("HISTFILE");
    if (historyFilePath is string)
    {
      foreach (var line in File.ReadAllLines(historyFilePath))
        history.Add($"    {history.Count + 1}  {line}");
      historyWriteIdx = history.Count;
    }

    FindExecutables();
    executables = [.. executablePaths.Keys];
    executables.Sort((a, b) => a.Length - b.Length);
    while (true)
    {
      Console.Write("$ ");
      string? line = ReadInputWithCompletion();

      if (line == null)
        continue;

      history.Add($"    {history.Count + 1}  {string.Join(" ", line)}");

      if (line.StartsWith("exit"))
      {
        if (historyFilePath is string)
        {
          var commands = history.Skip(historyWriteIdx).Select(line => line[(line.IndexOf("  ", 4) + 2)..]);
          File.AppendAllLines(historyFilePath, commands);
        }
        Environment.Exit(0);
      }

      List<string> args = ParseArgs(line);
      var state = ProcessRedirect(args);

      if (!state.Args.Contains("|"))
      {
        var (output, err) = RunExe(state.Args);
        WriteOutput(output, err, state);
      }
      else
      {
        int pipeIdx = state.Args.IndexOf("|"), startIdx = 0;
        string? input = null, output = null, err = null;
        while (true)
        {
          var pipedArgs = state.Args.Skip(startIdx).Take(pipeIdx - startIdx).ToList();
          if (pipeIdx != -1)
          {
            (input, err) = RunExe(pipedArgs, input);
            startIdx = pipeIdx + 1;
            pipeIdx = state.Args.IndexOf("|", startIdx);
          }
          else
          {
            pipedArgs = [.. state.Args.Skip(startIdx)];
            (output, err) = RunExe(pipedArgs, input);
            WriteOutput(output, err, state);
            break;
          }
        }
      }
    }
  }

  static (string? output, string? err) RunExe(List<string> args, string? input = null)
  {
    if (builtins.Contains(args.First()))
    {
      return RunBuiltins(args);
    }
    else if (executablePaths.ContainsKey(args.First()))
    {
      return RunExternal(args, input);
    }
    return (null, $"{args.First()}: command not found\n");
  }

  static string? ReadInputWithCompletion()
  {
    StringBuilder sb = new();
    bool showMultiple = false;
    bool fileCompletion = false;
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
        var line = sb.ToString();
        string prefix = line[(line.LastIndexOf(' ') + 1)..];

        var candidates = prefix.Length > 0 ? executables.Where(cmd => cmd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray() : [];
        fileCompletion = false;
        if (candidates.Length == 0 || prefix.Length == 0)
        {
          var cwd = Environment.CurrentDirectory;
          candidates = Directory.EnumerateFileSystemEntries(cwd, prefix + "*", SearchOption.TopDirectoryOnly)
          .Select(p => Path.GetRelativePath(cwd, p))
          .Select(p => Directory.Exists(p) ? p + Path.DirectorySeparatorChar : p)
          .OrderBy(x=> x.Length).ToArray();

          if (candidates.Length > 1)
          {
            var first = candidates[0].TrimEnd(Path.DirectorySeparatorChar);
            if (candidates.Skip(1).Any(x=> x.StartsWith(first)))
              candidates = [first];

            fileCompletion = candidates.Length > 1;
          }
        }

        if (candidates.Length == 0)
        {
          Console.Write("\a");
          continue;
        }

        var multi = (fileCompletion ? candidates : candidates.Where(cmd => cmd.Length == candidates.First().Length)).ToList();
        if (multi.Count > 1)
        {
          if (showMultiple)
          {
            Console.WriteLine();
            multi.Sort();
            Console.WriteLine(string.Join("  ", multi));
            Console.Write($"$ {sb}");
          }
          else
            Console.Write("\a");
          showMultiple = true;
        }
        else
        {
          bool isDirectory = candidates[0].EndsWith(Path.DirectorySeparatorChar) || Directory.Exists(Path.Combine(Environment.CurrentDirectory, candidates[0]));
          string trail = candidates.Length > 1 || isDirectory ? "" : " ";

          string completion = string.Concat(candidates.First().AsSpan(prefix.Length), trail);
          sb.Append(completion);
          Console.Write(completion);
        }

      }
      else if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow)
      {
        sb.Clear();
        Console.Write("\r");
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.Write("\r");

        historyIdx += (keyInfo.Key == ConsoleKey.DownArrow) ? -1 : 1;
        var historyLine = history[^historyIdx];
        int cmdStart = historyLine.IndexOf("  ", 4) + 2;
        string cmd = historyLine[cmdStart..];
        sb.Append(cmd);
        Console.Write($"$ {cmd}");
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
  static readonly string[] streamingCommands = ["tail", "watch", "top"];
  static (string? output, string? err) RunExternal(IReadOnlyList<string> args, string? input)
  {
    var startInfo = new ProcessStartInfo { FileName = args.First(), UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = input != null };
    foreach (var e in args.Skip(1).Where(a => !string.IsNullOrWhiteSpace(a)))
      startInfo.ArgumentList.Add(e);

    using var process = Process.Start(startInfo);
    if (process == null)
      return (null, null);

    if (input != null)
    {
      process.StandardInput.Write(input);
      process.StandardInput.Close();
    }

    if (streamingCommands.Contains(args[0]) && args.Any(a => a == "-f"))
    {
      Console.CancelKeyPress += (s, e) => { e.Cancel = true; process.Kill(); };
      process.StandardOutput.BaseStream.CopyTo(Console.OpenStandardOutput());
      process.WaitForExit();
      return (null, null);
    }

    string? output = process.StandardOutput.ReadToEnd();
    string? err = process.StandardError.ReadToEnd();
    process.WaitForExit();
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
    else if (command == "history")
    {
      if (args.Count == 3)
      {
        if (args[1] == "-r")
        {
          foreach (var line in File.ReadAllLines(args[2]))
            history.Add($"    {history.Count + 1}  {line}");
        }
        else if (args[1] == "-w" || args[1] == "-a")
        {
          var commands = history.Skip(historyWriteIdx).Select(line => line[(line.IndexOf("  ", 4) + 2)..]);
          File.AppendAllLines(args[2], commands);
          historyWriteIdx = history.Count;
        }
        return (null, null);
      }

      int limit = args.Count == 2 ? int.Parse(args[1]) : history.Count;
      return (string.Join('\n', history.Skip(history.Count - limit)) + '\n', null);
    }

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
  List<string> Args,
  string? OutPath = null,
  string? ErrPath = null,
  bool AppendOut = false,
  bool AppendErr = false
);
