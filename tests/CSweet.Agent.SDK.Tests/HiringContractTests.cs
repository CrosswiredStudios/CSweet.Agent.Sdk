namespace CSweet.Agent.SDK.Tests;

public sealed class HiringContractTests
{
    [Fact]
    public void RecommendationFulfillmentContracts_ExposeProgressAndPlanLineage()
    {
        var recommendationId = Guid.NewGuid();
        var sourceRequestId = Guid.NewGuid();
        var employeeIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var response = new HiringRecommendationResponse(
            recommendationId,
            null,
            "QA Engineer",
            "Own release quality.",
            "Approved",
            null,
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)
        {
            Headcount = 2,
            FulfilledHeadcount = 2,
            RemainingHeadcount = 0,
            SourceResourceChangeRequestId = sourceRequestId
        };
        var fulfilled = new HiringRecommendationFulfilledEvent(
            Guid.NewGuid(),
            recommendationId,
            sourceRequestId,
            Guid.NewGuid(),
            "product:qa",
            "QA Engineer",
            Guid.NewGuid(),
            null,
            2,
            2,
            employeeIds,
            DateTimeOffset.UtcNow);

        Assert.Equal(2, response.FulfilledHeadcount);
        Assert.Equal(0, response.RemainingHeadcount);
        Assert.Equal(sourceRequestId, fulfilled.SourceResourceChangeRequestId);
        Assert.Equal(employeeIds, fulfilled.ResultOrganizationUserIds);
        Assert.Null(typeof(UpsertHiringRecommendationRequest).GetProperty(nameof(response.FulfilledHeadcount)));
    }
}
