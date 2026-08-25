using System.Reflection;

namespace GhseeliApis.Tests.Integration;

internal static class BusinessAssemblyLoader
{
    private const string AssemblyName = "Ghseeli.BusinessApi";
    private static readonly Lock Sync = new();

    public static Assembly LoadFrom(string assemblyPath)
    {
        lock (Sync)
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(assembly =>
                    string.Equals(
                        assembly.GetName().Name,
                        AssemblyName,
                        StringComparison.Ordinal));
            return loaded ?? Assembly.LoadFrom(assemblyPath);
        }
    }
}
