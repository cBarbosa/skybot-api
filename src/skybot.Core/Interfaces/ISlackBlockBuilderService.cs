namespace skybot.Core.Interfaces;

public interface ISlackBlockBuilderService
{
    object[] CreateRemindersMenuBlocks(string userId);
    object CreateReminderModal(bool isForSomeone, string triggerId);
}

