using CSweet.WorkManagement.Contracts;
using Xunit;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformCalendarClientTests
{
    [Fact]
    public async Task CreateUsesTypedBrokerCapabilityAndPreservesIdempotency()
    {
        CreateCalendarEventRequest? captured = null;
        var input = new CalendarEventInput("Review", new DateTime(2026, 9, 8, 9, 0, 0), new DateTime(2026, 9, 8, 10, 0, 0));
        var expected = new CalendarEventView(Guid.NewGuid(), 1, Guid.NewGuid(), input, false, true);
        var runtime = new AgentTestRuntime().RegisterCapability<CreateCalendarEventRequest, CalendarEventView>(
            CalendarCapabilities.Create, (request, _) => { captured = request; return Task.FromResult(expected); });
        var result = await runtime.CreateContext().Platform.Calendar.CreateAsync(new(input, "calendar-stable-key"));
        Assert.Equal("calendar-stable-key", captured!.IdempotencyKey);
        Assert.Equal(input.Title, captured.Event.Title);
        Assert.Equal(expected.Id, result.Id);
    }

    [Fact]
    public async Task QueryUsesBusinessScopeFromRuntime()
    {
        CalendarQuery? captured = null;
        var runtime = new AgentTestRuntime().RegisterCapability<CalendarQuery, BusinessCalendarView>(
            CalendarCapabilities.Read, (request, _) => {
                captured = request;
                return Task.FromResult(new BusinessCalendarView("UTC", 1, Guid.NewGuid(), true, false, [], []));
            });
        var from = DateTimeOffset.UtcNow;
        await runtime.CreateContext().Platform.Calendar.ReadAsync(new(from, from.AddDays(7)));
        Assert.Equal(from, captured!.From);
    }
}