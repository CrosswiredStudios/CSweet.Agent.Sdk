using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>An owner's proposed work capacity. Null means unknown; zero means unavailable.</summary>
public sealed record WorkCapacityAssessment(decimal? OfferedPoints, string Rationale, string? MissingEvidence = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string State => OfferedPoints is null ? "Unknown" : OfferedPoints == 0 ? "Unavailable" : "Offered";

    public static WorkCapacityAssessment Validate(WorkCapacityAssessment value) =>
        value.OfferedPoints is not < 0 && !string.IsNullOrWhiteSpace(value.Rationale) &&
        (value.OfferedPoints.HasValue || !string.IsNullOrWhiteSpace(value.MissingEvidence))
            ? value : throw new AgentModelJsonException("Explain the capacity offer. Use null and name the material missing evidence when availability is unknown; zero means no work can be offered.");

    /// <summary>Reads additive capacity evidence without treating a legacy zero as proof of unavailability.</summary>
    public static WorkCapacityAssessment FromEvidence(JsonElement payload, decimal legacyPoints)
    {
        var envelope = payload.Deserialize<CapacityEnvelope>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (envelope?.CapacityAssessment is { } value)
        {
            Validate(value);
            if ((value.OfferedPoints ?? 0) != legacyPoints)
                throw new AgentModelJsonException("Capacity evidence conflicts with its numeric compatibility projection.");
            return value;
        }
        return legacyPoints > 0
            ? new(legacyPoints, "Retained accountable-owner capacity proposal.")
            : new(null, "The retained numeric zero does not distinguish unavailable capacity from missing evidence.",
                "Obtain a current owner assessment of availability and competing commitments.");
    }

    private sealed record CapacityEnvelope(WorkCapacityAssessment? CapacityAssessment);
}

/// <summary>Authorized owner-scoped observations. This is evidence for inference, never an allocation or grant.</summary>
public sealed record WorkAvailabilityContext(string Fingerprint, JsonElement Facts)
{
    public static async Task<WorkAvailabilityContext> ReadAsync(Guid boardId, Guid employeeId, Guid installationId,
        IReadOnlyCollection<Guid> proposedItems, AgentRuntimeContext context, AgentTeammate? teammate = null,
        CancellationToken ct = default)
    {
        var platform = context.Platform;
        var capabilities = (await platform.Tools.ListToolsAsync(ct)).Select(t => t.Capability).ToHashSet();
        var missing = new List<string>();
        var boards = new List<WorkBoardDetail> { await platform.Work.ReadBoardAsync(boardId, ct) };
        if (capabilities.Contains(WorkBoardCapabilities.Read))
        {
            var visible = (await platform.Work.ListBoardsAsync(cancellationToken: ct)).Where(b => !b.IsArchived && b.Id != boardId).OrderBy(b => b.Id).ToArray();
            foreach (var board in visible.Take(31))
            {
                try { boards.Add(await platform.Work.ReadBoardAsync(board.Id, ct)); }
                catch (PlatformCapabilityException error) when (error.Code is PlatformCapabilityErrorCode.Denied or PlatformCapabilityErrorCode.NotFound)
                { missing.Add("A visible work board could not be read: " + board.Id); }
            }
            if (visible.Length > 31) missing.Add("Additional boards exceed this bounded observation; commitments outside the observed boards may exist.");
        }
        else missing.Add("Cross-board assignments are not readable with the current capabilities.");

        var agenda = await platform.PersonalTodo.ListAsync(ct);
        // A manager's directory can contain other employees' boards. They are not this owner's workload.
        var ownWork = agenda.Boards.Where(b => b.OwnerOrganizationUserId == employeeId).SelectMany(b => b.Items)
            .Where(i => i.ArchivedAt is null && i.IsExecutable && i.Status is not ("Completed" or "Cancelled"))
            .OrderBy(i => i.Id).ToArray();
        bool RelatedCoordination(PersonalTodoItem item) => item.WorkContext?.BoardId == boardId && item.WorkContext.WorkItemId is null;
        var commitments = ownWork.Where(i => !RelatedCoordination(i))
            .Select(i => new { i.Id, i.Title, i.Status, i.WorkContext, waiting = i.Wait is not null }).ToArray();
        var assignments = boards.SelectMany(b => b.Items.Select(i => new { boardId = b.Board?.Id, item = i }))
            .Where(x => x.item.ExecutionMode == WorkItemExecutionModes.Executable && x.item.Status is not ("Done" or "Completed" or "Cancelled") &&
                (x.item.AssignedEmployeeId == employeeId || x.item.AssignedInstallationId == installationId ||
                 x.item.StageAssignments.Any(a => a.OrganizationUserId == employeeId || a.AgentInstallationId == installationId)))
            .Where(x => !proposedItems.Contains(x.item.Id))
            .OrderBy(x => x.item.Id).Select(x => new { x.boardId, x.item.Id, x.item.Title, x.item.Status,
                x.item.SprintId, x.item.EstimatePoints, x.item.PlanningRevision }).ToArray();

        var from = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var until = from.AddDays(30);
        if (capabilities.Contains(WorkSprintCapabilities.Read))
        {
            var planned = (await platform.Work.ListSprintsAsync(boardId, ct)).Where(s => s.Status == "Planned")
                .OrderBy(s => s.Sequence).ThenBy(s => s.Id).FirstOrDefault();
            if (planned?.EndsAt > from) until = planned.EndsAt.Value;
        }
        CalendarOccurrence[] calendar = [];
        var calendarReadable = capabilities.Contains(CalendarCapabilities.Read);
        if (calendarReadable)
        {
            try {
                var view = await platform.Calendar.ReadAsync(new(from, until), ct);
                calendar = view.Occurrences.Where(o => !o.Series.Cancelled &&
                    (o.Series.OwnerOrganizationUserId == employeeId || o.Event.AttendeeIds?.Contains(employeeId) == true ||
                     o.Event.Work?.TargetOrganizationUserId == employeeId)).OrderBy(o => o.EventId).ThenBy(o => o.Start).ToArray();
            }
            catch (PlatformCapabilityException error) when (error.Code is PlatformCapabilityErrorCode.Denied or PlatformCapabilityErrorCode.NotFound)
            { calendarReadable = false; }
        }
        if (!calendarReadable) missing.Add("The owner's calendar could not be read. This is unknown calendar coverage, not zero availability.");
        var owner = teammate ?? context.Identity?.TeamContext?.Members.SingleOrDefault(m => m.EmployeeId == employeeId.ToString());
        var material = new { boardId, employeeId, installationId,
            owner = owner is null ? null : new { owner.IsAvailable, owner.RuntimeEligibility, owner.DeclaredRoleKeys },
            proposedItems = proposedItems.Order().ToArray(), competingAssignments = assignments, personalCommitments = commitments,
            calendarReadable, calendar = calendar.Select(o => new { o.EventId, o.Series.Revision, o.Start, o.End, o.Event.Title,
                o.Event.Description, o.Event.Work }), missing };
        var fingerprint = AgentOperatingStateKey.Create("work-availability:v1", material);
        return new(fingerprint, JsonSerializer.SerializeToElement(new { material,
            relatedCoordination = ownWork.Where(RelatedCoordination).Select(i => new { i.Id, i.Title, i.Status }),
            calendarInspectionWindow = new { from, until },
            interpretation = "This is an observation window, not a required timebox or an allocation. An empty calendar is neither zero capacity nor unlimited capacity. Proposed items are demand, not competing committed work. Judge the owner's sustainable offer from actual commitments, capability and uncertainty. Related coordination is overhead for this same board; advancing its bookkeeping does not create another independent workload or invalidate the offer. Manager/employee planning coordination is not completed delivery or an execution grant." }));
    }
}
