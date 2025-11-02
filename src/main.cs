using System.Collections;

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
        Console.WriteLine(line.AsMemory(5));

      else if (firstToken == "type")
      {
        Type(line);
      }
      else
        Console.WriteLine($"{line}: command not found");
    }
  }

  static void Type(string line)
  {
    string secondToken = line[5..];
    if (builtins.Contains(secondToken))
      Console.WriteLine($"{secondToken} is a shell builtin");
    else
    {
      string? path = Environment.GetEnvironmentVariable("PATH");
      foreach (var dir in path!.Split(":"))
      {
        var fullPath = Path.Combine(dir, secondToken);
        if (File.Exists(fullPath) && IsExecutable(fullPath))
        {
          Console.Error.WriteLine($"{secondToken} is {fullPath}");
          return;
        }
      }
      Console.WriteLine($"{secondToken}: not found");
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

