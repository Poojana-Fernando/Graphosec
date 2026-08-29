namespace ResumableCopy.Application.Abstractions;

public interface IUserPromptService
{
    bool Confirm(string title, string message);
}
