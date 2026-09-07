namespace CSweet.Agent.SDK;

public static class PluginSetupEvents
{
    public const string Requested = "com.csweet.plugin.setup.requested.v1";
}

/// <summary>Exact-installation setup assistance. Only the host supplies participant and conversation identities.</summary>
public sealed record PluginSetupRequestedEvent(Guid OrganizationId, Guid InstallationId,
    Guid AgentOrganizationUserId, Guid HumanOrganizationUserId, Guid ConversationId,
    DateTimeOffset RequestedAt, bool Reminder = false);
