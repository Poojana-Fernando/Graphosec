namespace ResumableCopy.Application.Services;

public sealed class DefaultUserPromptService : Abstractions.IUserPromptService
{
    public bool Confirm(string title, string message) => true;
}
