namespace VoxInject.Core.Services;

public interface ITextInjectionService
{
    void Inject(string text, bool appendEnter = false, bool shiftEnter = false);
}
