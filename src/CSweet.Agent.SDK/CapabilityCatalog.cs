using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>
/// Canonical serialized capability names understood by C-Sweet.
/// All SDK capability helpers must reference these constants rather than repeat wire strings.
/// </summary>
public static class CapabilityNames
{
    public static class Assistant
    {
        public const string Converse = "assistant.converse.v1";
        public const string SummarizeActivity = "assistant.summarize-activity.v1";
        public const string PlanWork = "assistant.plan-work.v1";
    }

    public static class Agent
    {
        public const string ConfigurationDescribe = "agent.configuration.describe.v1";
        public const string ConfigurationUpdate = "agent.configuration.update.v1";
        public const string CompleteOnboarding = "agent.onboarding.complete.v1";
    }

    public static class Platform
    {
        public const string LlmChatStream = "platform.llm.chat-stream.v1";
        public const string BusinessProfileRead = "platform.business-profile.read.v1";
        public const string BusinessProfileUpdateExplicit = "platform.business-profile.update-explicit.v1";
        public const string BusinessProfileProposeUpdate = "platform.business-profile.propose-update.v1";
        public const string OrganizationSnapshotRead = "platform.organization.snapshot.read.v1";
        public const string BusinessPatternSearch = "platform.business-pattern.search.v1";
        public const string WorkstreamPlanPropose = "platform.workstream.plan.propose.v1";
        public const string WorkforceSearch = "platform.workforce.search.v1";
        public const string WorkforcePlanPropose = "platform.workforce-plan.propose.v1";
        public const string FinanceProfileRead = "platform.finance-profile.read.v1";
        public const string FinanceProfileProposeUpdate = "platform.finance-profile.propose-update.v1";
        public const string BudgetEvaluate = "platform.budget.evaluate.v1";
        public const string ApprovalPropose = "platform.approval.propose.v1";
        public const string ManagementCycleRead = "platform.management-cycle.read.v1";
        public const string UserInputRequest = "platform.user-input.request.v1";
        public const string HiringRecommendationList = "platform.hiring-recommendation.list.v1";
        public const string HiringRecommendationUpsert = "platform.hiring-recommendation.upsert.v1";
        public const string HiringRecommendationResolve = "platform.hiring-recommendation.resolve.v1";
        public const string HiringRecommendationWithdraw = "platform.hiring-recommendation.withdraw.v1";
        public const string ResourceChangePropose = "platform.management.resource-change.propose.v1";
        public const string ResourceChangeRead = "platform.management.resource-change.read.v1";
        public const string ResourceChangeDecide = "platform.management.resource-change.decide.v1";
        public const string HiringWorkflowStage = "platform.hiring-workflow.stage.v1";
        public const string UserActionSuggest = "platform.user-action.suggest.v1";
        public const string TeamRosterRead = "platform.team-roster.read.v1";
    }

    public static class AgentCatalog
    {
        public const string Search = "platform.agent-catalog.search.v1";
    }

    public static class Memory
    {
        public const string BusinessRead = "memory.business.read.v1";
        public const string BusinessPropose = "memory.business.propose.v1";
        public const string UserRead = "memory.user.read.v1";
        public const string UserPropose = "memory.user.propose.v1";
    }

    public static class Communication
    {
        public const string ChatRead = "communication.chat.read.v1";
        public const string ChatCreate = "communication.chat.create.v1";
        public const string ChatModify = "communication.chat.modify.v1";
        public const string ChatDelete = "communication.chat.delete.v1";
        public const string MessageSend = "communication.message.send.v1";
        public const string MessageIngest = "communication.message.ingest.v1";
        public const string Send = "communication.send.v1";
        public const string WorkspaceApply = "communication.workspace.apply.v1";
        public const string IdentityAssign = "communication.identity.assign.v1";
        public const string LinkCodeRegister = "communication.link-code.register.v1";
    }

    public static class Management
    {
        public const string CheckIn = "management.check-in.v1";
        public const string ProductRoleBrief = "management.product-role-brief.v1";
        public const string ProductPlanReview = "management.product-plan.review.v1";
        public const string ProductEscalation = "management.product-escalation.v1";
    }

    public static class ProductManagement
    {
        public const string Plan = "product-management.plan.v1";
        public const string ContextUpdate = "product-management.context.update.v1";
    }

    public static class WorkManagement
    {
        public const string BoardRead = WorkManagementCapabilityNames.BoardRead;
        public const string BoardCreate = WorkManagementCapabilityNames.BoardCreate;
        public const string ItemRead = WorkManagementCapabilityNames.ItemRead;
        public const string ItemStart = WorkManagementCapabilityNames.ItemStart;
        public const string ItemCreate = WorkManagementCapabilityNames.ItemCreate;
        public const string ItemComment = WorkManagementCapabilityNames.ItemComment;
        public const string ItemEstimate = WorkManagementCapabilityNames.ItemEstimate;
        public const string ItemMove = WorkManagementCapabilityNames.ItemMove;
        public const string ItemComplete = WorkManagementCapabilityNames.ItemComplete;
        public const string ItemCancel = WorkManagementCapabilityNames.ItemCancel;
        public const string ItemReopen = WorkManagementCapabilityNames.ItemReopen;
        public const string ItemTransfer = WorkManagementCapabilityNames.ItemTransfer;
        public const string ItemQualitySubmit = WorkManagementCapabilityNames.ItemQualitySubmit;
        public const string SprintRead = WorkManagementCapabilityNames.SprintRead;
        public const string SprintCreate = WorkManagementCapabilityNames.SprintCreate;
        public const string SprintStart = WorkManagementCapabilityNames.SprintStart;
        public const string SprintComplete = WorkManagementCapabilityNames.SprintComplete;
        public const string SprintCancel = WorkManagementCapabilityNames.SprintCancel;
        public const string SprintManageScope = WorkManagementCapabilityNames.SprintManageScope;
        public const string SprintManageCapacity = WorkManagementCapabilityNames.SprintManageCapacity;
        public const string SprintCarryOver = WorkManagementCapabilityNames.SprintCarryOver;
        public const string SprintReadReports = WorkManagementCapabilityNames.SprintReadReports;
        public const string AutomationRead = WorkManagementCapabilityNames.AutomationRead;
        public const string AutomationManage = WorkManagementCapabilityNames.AutomationManage;
        public const string ExecutionRunV1 = WorkManagementCapabilityNames.ExecutionRunV1;
        public const string OrchestrationRead = WorkManagementCapabilityNames.OrchestrationRead;
        public const string OrchestrationPreflight = WorkManagementCapabilityNames.OrchestrationPreflight;
        public const string OrchestrationStart = WorkManagementCapabilityNames.OrchestrationStart;
        public const string OrchestrationPause = WorkManagementCapabilityNames.OrchestrationPause;
        public const string OrchestrationResume = WorkManagementCapabilityNames.OrchestrationResume;
        public const string OrchestrationCancel = WorkManagementCapabilityNames.OrchestrationCancel;
        public const string OrchestrationRetry = WorkManagementCapabilityNames.OrchestrationRetry;
    }

    public static class Web
    {
        public const string Fetch = "web.fetch.v1";
        public const string Request = "web.request.v1";
        public const string Render = "web.render.v1";
        public const string Socket = "web.socket.v1";
    }

    public static class Plugin
    {
        public const string State = "plugin.state.v1";
    }

    public static class GitWorkspace
    {
        public const string Prepare = "git.workspace.prepare.v1";
        public const string Inspect = "git.workspace.inspect.v1";
        public const string Publish = "git.workspace.publish.v1";
        public const string Cleanup = "git.workspace.cleanup.v1";
    }
}

/// <summary>
/// Authoritative capability catalog, organized by the service or feature that owns each wire name.
/// </summary>
public static class CapabilityCatalog
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ByService { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["assistant"] = Set(
                CapabilityNames.Assistant.Converse,
                CapabilityNames.Assistant.SummarizeActivity,
                CapabilityNames.Assistant.PlanWork),
            ["agent"] = Set(
                CapabilityNames.Agent.ConfigurationDescribe,
                CapabilityNames.Agent.ConfigurationUpdate,
                CapabilityNames.Agent.CompleteOnboarding),
            ["agent-catalog"] = Set(
                CapabilityNames.AgentCatalog.Search),
            ["platform"] = Set(
                CapabilityNames.Platform.LlmChatStream,
                CapabilityNames.Platform.BusinessProfileRead,
                CapabilityNames.Platform.BusinessProfileUpdateExplicit,
                CapabilityNames.Platform.BusinessProfileProposeUpdate,
                CapabilityNames.Platform.OrganizationSnapshotRead,
                CapabilityNames.Platform.BusinessPatternSearch,
                CapabilityNames.Platform.WorkstreamPlanPropose,
                CapabilityNames.Platform.WorkforceSearch,
                CapabilityNames.Platform.WorkforcePlanPropose,
                CapabilityNames.Platform.FinanceProfileRead,
                CapabilityNames.Platform.FinanceProfileProposeUpdate,
                CapabilityNames.Platform.BudgetEvaluate,
                CapabilityNames.Platform.ApprovalPropose,
                CapabilityNames.Platform.ManagementCycleRead,
                CapabilityNames.Platform.UserInputRequest,
                CapabilityNames.Platform.HiringRecommendationList,
                CapabilityNames.Platform.HiringRecommendationUpsert,
                CapabilityNames.Platform.HiringRecommendationResolve,
                CapabilityNames.Platform.HiringRecommendationWithdraw,
                CapabilityNames.Platform.ResourceChangePropose,
                CapabilityNames.Platform.ResourceChangeRead,
                CapabilityNames.Platform.ResourceChangeDecide,
                CapabilityNames.Platform.UserActionSuggest,
                CapabilityNames.Platform.HiringWorkflowStage,
                CapabilityNames.Platform.TeamRosterRead),
            ["memory"] = Set(
                CapabilityNames.Memory.BusinessRead,
                CapabilityNames.Memory.BusinessPropose,
                CapabilityNames.Memory.UserRead,
                CapabilityNames.Memory.UserPropose),
            ["communication"] = Set(
                CapabilityNames.Communication.ChatRead,
                CapabilityNames.Communication.ChatCreate,
                CapabilityNames.Communication.ChatModify,
                CapabilityNames.Communication.ChatDelete,
                CapabilityNames.Communication.MessageSend,
                CapabilityNames.Communication.MessageIngest,
                CapabilityNames.Communication.Send,
                CapabilityNames.Communication.WorkspaceApply,
                CapabilityNames.Communication.IdentityAssign,
                CapabilityNames.Communication.LinkCodeRegister),
            ["management"] = Set(
                CapabilityNames.Management.CheckIn,
                CapabilityNames.Management.ProductRoleBrief,
                CapabilityNames.Management.ProductPlanReview,
                CapabilityNames.Management.ProductEscalation),
            ["product-management"] = Set(
                CapabilityNames.ProductManagement.Plan,
                CapabilityNames.ProductManagement.ContextUpdate),
            ["work-management"] = Set(
                CapabilityNames.WorkManagement.BoardRead,
                CapabilityNames.WorkManagement.BoardCreate,
                CapabilityNames.WorkManagement.ItemRead,
                CapabilityNames.WorkManagement.ItemCreate,
                CapabilityNames.WorkManagement.ItemComment,
                CapabilityNames.WorkManagement.ItemEstimate,
                CapabilityNames.WorkManagement.ItemTransfer,
                CapabilityNames.WorkManagement.SprintRead,
                CapabilityNames.WorkManagement.SprintCreate,
                CapabilityNames.WorkManagement.SprintManageScope,
                CapabilityNames.WorkManagement.SprintManageCapacity,
                CapabilityNames.WorkManagement.SprintCarryOver,
                CapabilityNames.WorkManagement.SprintReadReports,
                CapabilityNames.WorkManagement.OrchestrationRead,
                CapabilityNames.WorkManagement.OrchestrationPreflight,
                CapabilityNames.WorkManagement.OrchestrationStart,
                CapabilityNames.WorkManagement.OrchestrationPause,
                CapabilityNames.WorkManagement.OrchestrationResume,
                CapabilityNames.WorkManagement.OrchestrationCancel,
                CapabilityNames.WorkManagement.OrchestrationRetry,
                CapabilityNames.WorkManagement.ExecutionRunV1),
            ["git-workspace"] = Set(
                CapabilityNames.GitWorkspace.Prepare,
                CapabilityNames.GitWorkspace.Inspect,
                CapabilityNames.GitWorkspace.Publish,
                CapabilityNames.GitWorkspace.Cleanup),
            ["web"] = Set(
                CapabilityNames.Web.Fetch,
                CapabilityNames.Web.Request,
                CapabilityNames.Web.Render,
                CapabilityNames.Web.Socket),
            ["plugin"] = Set(CapabilityNames.Plugin.State)
        };

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        ByService.Values.SelectMany(x => x),
        StringComparer.Ordinal);

    public static bool IsKnown(string capability) =>
        !string.IsNullOrWhiteSpace(capability) && All.Contains(capability);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}

public static class AssistantCapabilities
{
    public const string Converse = CapabilityNames.Assistant.Converse;
    public const string SummarizeActivity = CapabilityNames.Assistant.SummarizeActivity;
    public const string PlanWork = CapabilityNames.Assistant.PlanWork;
}

/// <summary>Read-only discovery capabilities for installable and installed agents.</summary>
public static class AgentCatalogCapabilities
{
    public const string Search = CapabilityNames.AgentCatalog.Search;
}

public static class AgentLifecycleCapabilities
{
    public const string CompleteOnboarding = CapabilityNames.Agent.CompleteOnboarding;
}

public static class CommunicationCapabilities
{
    public const string ChatRead = CapabilityNames.Communication.ChatRead;
    public const string ChatCreate = CapabilityNames.Communication.ChatCreate;
    public const string ChatModify = CapabilityNames.Communication.ChatModify;
    public const string ChatDelete = CapabilityNames.Communication.ChatDelete;
    public const string MessageSend = CapabilityNames.Communication.MessageSend;
    public const string MessageIngest = CapabilityNames.Communication.MessageIngest;
    public const string Send = CapabilityNames.Communication.Send;
    public const string WorkspaceApply = CapabilityNames.Communication.WorkspaceApply;
    public const string IdentityAssign = CapabilityNames.Communication.IdentityAssign;
    public const string LinkCodeRegister = CapabilityNames.Communication.LinkCodeRegister;
}

public static class MemoryCapabilities
{
    public const string BusinessRead = CapabilityNames.Memory.BusinessRead;
    public const string BusinessPropose = CapabilityNames.Memory.BusinessPropose;
    public const string UserRead = CapabilityNames.Memory.UserRead;
    public const string UserPropose = CapabilityNames.Memory.UserPropose;
}

public static class ProductManagementCapabilities
{
    public const string RoleBrief = CapabilityNames.Management.ProductRoleBrief;
    public const string PlanReview = CapabilityNames.Management.ProductPlanReview;
    public const string Escalation = CapabilityNames.Management.ProductEscalation;
    public const string Plan = CapabilityNames.ProductManagement.Plan;
    public const string ContextUpdate = CapabilityNames.ProductManagement.ContextUpdate;
}

public static class WorkBoardCapabilities
{
    public const string Read = CapabilityNames.WorkManagement.BoardRead;
    public const string Create = CapabilityNames.WorkManagement.BoardCreate;
}

public static class WorkItemCapabilities
{
    public const string Read = CapabilityNames.WorkManagement.ItemRead;
    public const string Start = CapabilityNames.WorkManagement.ItemStart;
    public const string Create = CapabilityNames.WorkManagement.ItemCreate;
    public const string Comment = CapabilityNames.WorkManagement.ItemComment;
    public const string Estimate = CapabilityNames.WorkManagement.ItemEstimate;
    public const string Move = CapabilityNames.WorkManagement.ItemMove;
    public const string Complete = CapabilityNames.WorkManagement.ItemComplete;
    public const string Cancel = CapabilityNames.WorkManagement.ItemCancel;
    public const string Reopen = CapabilityNames.WorkManagement.ItemReopen;
    public const string Transfer = CapabilityNames.WorkManagement.ItemTransfer;
    public const string QualitySubmit = CapabilityNames.WorkManagement.ItemQualitySubmit;
}

/// <summary>Ticket-scoped Git workspace operations executed inside the agent runtime.</summary>
public static class GitWorkspaceCapabilities
{
    public const string Prepare = CapabilityNames.GitWorkspace.Prepare;
    public const string Inspect = CapabilityNames.GitWorkspace.Inspect;
    public const string Publish = CapabilityNames.GitWorkspace.Publish;
    public const string Cleanup = CapabilityNames.GitWorkspace.Cleanup;
}

public static class WorkSprintCapabilities
{
    public const string Read = CapabilityNames.WorkManagement.SprintRead;
    public const string Create = CapabilityNames.WorkManagement.SprintCreate;
    public const string Start = CapabilityNames.WorkManagement.SprintStart;
    public const string Complete = CapabilityNames.WorkManagement.SprintComplete;
    public const string Cancel = CapabilityNames.WorkManagement.SprintCancel;
    public const string ManageScope = CapabilityNames.WorkManagement.SprintManageScope;
    public const string ManageCapacity = CapabilityNames.WorkManagement.SprintManageCapacity;
    public const string CarryOver = CapabilityNames.WorkManagement.SprintCarryOver;
    public const string ReadReports = CapabilityNames.WorkManagement.SprintReadReports;
}

public static class WorkAutomationCapabilities
{
    public const string Read = CapabilityNames.WorkManagement.AutomationRead;
    public const string Manage = CapabilityNames.WorkManagement.AutomationManage;
}

public static class WorkOrchestrationCapabilities
{
    public const string Read = CapabilityNames.WorkManagement.OrchestrationRead;
    public const string Preflight = CapabilityNames.WorkManagement.OrchestrationPreflight;
    public const string Start = CapabilityNames.WorkManagement.OrchestrationStart;
    public const string Pause = CapabilityNames.WorkManagement.OrchestrationPause;
    public const string Resume = CapabilityNames.WorkManagement.OrchestrationResume;
    public const string Cancel = CapabilityNames.WorkManagement.OrchestrationCancel;
    public const string Retry = CapabilityNames.WorkManagement.OrchestrationRetry;
    public const string Execute = CapabilityNames.WorkManagement.ExecutionRunV1;
}

public static class WebCapabilities
{
    public const string Fetch = CapabilityNames.Web.Fetch;
    public const string Request = CapabilityNames.Web.Request;
    public const string Render = CapabilityNames.Web.Render;
    public const string Socket = CapabilityNames.Web.Socket;
}

public static class PluginCapabilities
{
    public const string State = CapabilityNames.Plugin.State;
}
