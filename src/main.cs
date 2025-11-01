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

      int spaceIdx = line.IndexOf(' ');
      string command = spaceIdx < 0 ? line : line[0..spaceIdx];

      if (command == "echo")
        Console.WriteLine(line.AsMemory(5));

      else if (command == "type")
      {
        string cmd2 = line[(spaceIdx + 1)..];
        if (builtins.Contains(cmd2))
          Console.WriteLine($"{cmd2} is a shell builtin");
        else
          Console.WriteLine($"{cmd2}: not found");
      }
      else
        Console.WriteLine($"{line}: command not found");
    }
  }
}
