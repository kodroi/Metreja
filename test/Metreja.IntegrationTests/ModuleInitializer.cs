using System.Runtime.CompilerServices;

namespace Metreja.IntegrationTests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Verifier.DerivePathInfo(
            (sourceFile, projectDirectory, type, method) => new(
                directory: Path.Combine(projectDirectory, "Snapshots"),
                typeName: type.Name,
                methodName: method.Name));
        VerifierSettings.DontScrubDateTimes();
    }
}
