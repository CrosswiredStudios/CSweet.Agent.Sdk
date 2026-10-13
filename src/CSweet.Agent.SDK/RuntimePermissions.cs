namespace CSweet.Agent.SDK;

/// <summary>Installation grants that configure execution, rather than callable broker tools.</summary>
public static class RuntimePermissions
{
    public const string PublicInternet = "runtime.internet.public.v1";
    public static bool IsExecutionPermission(string name) => name == PublicInternet;
}
