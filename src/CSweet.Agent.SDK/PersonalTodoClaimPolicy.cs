using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Opt-in review performed before the SDK atomically claims a Ready personal item.</summary>
public interface IPersonalTodoClaimPolicy
{
    Task<PersonalTodoClaimDecision> EvaluatePersonalTodoClaimAsync(
        PersonalTodoItem item,
        AgentRuntimeContext context,
        CancellationToken cancellationToken);
}

public enum PersonalTodoClaimDecision
{
    Skip,
    Claim
}
