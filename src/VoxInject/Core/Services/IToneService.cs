namespace VoxInject.Core.Services;

public interface IToneService
{
    void PlayActivation(double volume);
    void PlayDeactivation(double volume);
}
