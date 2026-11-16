using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Separates execution environments from product targets for inference-based feasibility review.</summary>
public static class ToolchainReviewContext
{
    public static object Describe(EligibleToolchainAdapter adapter, string recipeKey)
    {
        var recipe = adapter.Definition.Recipes.Single(r => r.Key == recipeKey);
        return new {
            adapter.Definition.DisplayName, adapter.Definition.RequiredExecutableVersions,
            recipe = new { recipe.Key, recipe.Operations, recipe.TargetKeys, recipe.ConfigurationSchema },
            certifiedEnvironment = adapter.Eligibility,
            supportedBuildEnvironmentAlternatives = recipe.RequiredEnvironmentProfileKeys,
            interpretation = "Each eligible entry is an independently certified build environment. The listed environment profiles are supported alternatives, not a requirement to provision all operating systems. Product targets and requested acceptance coverage are separate. Judge whether the offered environment satisfies the actual accepted work; identify a specific unmet obligation before requiring another environment."
        };
    }
}
