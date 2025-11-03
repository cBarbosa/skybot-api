namespace skybot.Models;

internal record Command(string Name, string Description, Func<SlackEvent, string, Task> Action);