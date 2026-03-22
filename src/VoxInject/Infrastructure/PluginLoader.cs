using System.Reflection;
using VoxInject.Providers.Abstractions;

namespace VoxInject.Infrastructure;

/// <summary>
/// Scans the <c>plugins/</c> directory next to the executable and loads every
/// DLL that matches <c>VoxInject.Providers.*.dll</c>.
/// Each DLL is expected to export at least one <see cref="ITranscriptionProvider"/>
/// implementation with a public parameterless constructor.
/// </summary>
public static class PluginLoader
{
    public static IReadOnlyList<ITranscriptionProvider> Load(string pluginsDir)
    {
        var providers = new List<ITranscriptionProvider>();

        if (!Directory.Exists(pluginsDir))
            return providers;

        foreach (var dll in Directory.GetFiles(pluginsDir, "VoxInject.Providers.*.dll",
                     System.IO.SearchOption.TopDirectoryOnly))
        {
            try
            {
                var asm = Assembly.LoadFrom(dll);
                foreach (var type in asm.GetExportedTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(ITranscriptionProvider).IsAssignableFrom(type)) continue;
                    if (Activator.CreateInstance(type) is ITranscriptionProvider p)
                        providers.Add(p);
                }
            }
            catch
            {
                // Malformed or incompatible plugin — skip silently.
            }
        }

        return providers;
    }
}
