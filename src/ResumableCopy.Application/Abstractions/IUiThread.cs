namespace ResumableCopy.Application.Abstractions;

public interface IUiThread
{
    void Invoke(Action action);

    void Post(Action action);
}
