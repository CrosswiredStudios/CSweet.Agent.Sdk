# Example Agent

Processes a bounded request for C-Sweet.

## Contract

- Package ID: `com.example.agent`
- Version: `0.1.0`
- Provides: `example.execute.v1`
- Activation: manual
- Requested platform/provider capabilities: none
- Event subscriptions: none
- Network access: none

## Develop

```powershell
dotnet test
dotnet run --project src/CSweet.Agent.Template -- --self-test
```

The tests run entirely in memory and require no C-Sweet instance or credentials.

## Install

Keep `csweet-plugin.json` at the repository root. Import a reviewed GitHub commit in C-Sweet, or
clone this repository as an immediate child of C-Sweet's configured local agent catalog. Review
the exact manifest, grants, activation mode, and source before approving installation.

Built with `CSweet.Agent.SDK` 3.40.0.

## Business calendars

Use context.Platform.Calendar.ReadAsync, CreateAsync, UpdateAsync, CancelAsync, or
ScheduleAsync with the typed work-management calendar contracts. Requests are bound to the
runtime business and employee; permission declarations require upgrade review.
Use local wall-clock dates without offsets, an explicit time zone, stable creation keys, and
the last observed revision for edits. Contributors edit their own events; managers can edit all.
Scheduling others follows the reporting hierarchy and never expands execution authority.

Subscribe to com.csweet.calendar.reminder-due.v1 and override HandleCalendarReminderAsync
when role-specific reminder behavior is needed. The default callback reports receipt; scheduled
work is delivered separately through the existing personal work queue.
Calendar.WithToolsAsync(options) adds only approved calendar model tools and operating guidance
to an existing harness. Calendar.GetResponseAsync(client, messages, ...) supplies a bounded
function-invocation loop for simple agents. Preserve all existing execution and approval rules.