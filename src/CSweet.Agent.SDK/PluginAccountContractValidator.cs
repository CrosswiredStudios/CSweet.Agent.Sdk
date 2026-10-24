using System.Text.RegularExpressions;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK;

/// <summary>Shared SDK/host validation. Descriptors never grant account or business authority.</summary>
public static class PluginAccountContractValidator
{
    private static readonly Regex Id = new(@"\A[a-z][a-z0-9.-]{0,198}[a-z0-9]\z", RegexOptions.CultureInvariant);
    private static readonly Regex Pointer = new(@"\A(?:/[A-Za-z0-9_-]+)+\z", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Validate(AgentManifest manifest)
    {
        var errors = new List<string>();
        if (manifest.Accounts is not { } a) return errors;
        if (manifest.Protocol?.MinimumVersion != "2.4" || manifest.Kind != "connector")
            errors.Add("Enterprise accounts require a protocol 2.4 connector.");
        if (a.Permissions is null || a.PolicyLinks is null || a.Permissions.Any(x => x is null) ||
            a.Permissions.Any(x => x.Capabilities is null || x.ScopeSets is null))
        {
            errors.Add("Account permissions and policy links cannot be null.");
            return errors;
        }
        if (a.Layout != "accounts-and-resources.v1" || string.IsNullOrEmpty(a.ResourceType) || !Id.IsMatch(a.ResourceType) || a.MaximumResources is < 1 or > 10000)
            errors.Add("Account management requires the native layout, a resource type and bounded resource collection.");
        foreach (var label in new[] { a.AccountLabel, a.AccountsLabel, a.ResourceLabel, a.ResourcesLabel, a.ConsentPurpose, a.PolicyVersion })
            if (string.IsNullOrWhiteSpace(label) || label.Length > 1000 || label.Any(char.IsControl))
                errors.Add("Account labels, consent purpose and policy version must be bounded plain text.");
        if (!Https(a.PrivacyPolicyUrl) || !Https(a.TermsOfServiceUrl) || a.PolicyLinks.Count > 8 ||
            a.PolicyLinks.Any(x => x is null || !Https(x.Url) || string.IsNullOrWhiteSpace(x.Label) || x.Label.Length > 100))
            errors.Add("Account policy links require public HTTPS URLs and bounded labels.");
        var matches = manifest.Connections.Where(x => x.Id == a.Connection).ToArray();
        var connection = matches.Length == 1 ? matches[0] : null;
        if (connection is null || manifest.Setup is not { Required: false } setup ||
            setup.Flows.Count(f => f.Id == a.SetupFlow) != 1 ||
            !setup.Flows.Where(f => f.Id == a.SetupFlow).SelectMany(f => f.Steps).Any(s =>
                s.Kind is "oauth-connect" or "permission-request" && s.Connection == a.Connection && connection.ScopeSets.Any(x => x.Id == s.ScopeSet)))
            errors.Add("Account management must reference a declared connection and setup flow.");
        bool Bootstrap(string capability) => manifest.ProviderOperations.Any(x => x.Capability == capability &&
            x.Effect == "read" && x.Http is { Bootstrap: true, Method: "GET" } http && http.Connection == a.Connection &&
            http.MediaInput is null && http.SecretResponseFields.Count == 0 && http.BoundResourceQuery is null);
        if (!Bootstrap(a.DiscoveryCapability)) errors.Add("Resource discovery requires a declared, read-only bootstrap operation.");
        var p = a.ResourceProjection;
        if (p is null || !Path(p.ItemsPointer) || !Path(p.IdPointer) || !Path(p.NamePointer) ||
            p.HandlePointer is not null && !Path(p.HandlePointer) || p.NextPageTokenPointer is not null && !Path(p.NextPageTokenPointer))
            errors.Add("Resource discovery requires bounded data projection pointers.");
        var discovery = manifest.ProviderOperations.FirstOrDefault(x => x.Capability == a.DiscoveryCapability);
        if ((a.PageTokenInput is null) != (p?.NextPageTokenPointer is null) ||
            a.PageTokenInput is not null && !ValidPageToken(discovery, a.PageTokenInput))
            errors.Add("Pagination requires an optional bounded string input and a continuation projection.");
        if (a.Identity is { } identity && (!Bootstrap(identity.Capability) || !Path(identity.SubjectPointer) ||
            identity.NamePointer is not null && !Path(identity.NamePointer) || identity.EmailPointer is not null && !Path(identity.EmailPointer)))
            errors.Add("Identity requires a reviewed authenticated bootstrap read and bounded projection pointers.");
        if (a.Permissions.Count is < 1 or > 64 || a.Permissions.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != a.Permissions.Count)
            errors.Add("Account permissions must be unique and bounded.");
        foreach (var permission in a.Permissions)
        {
            if (string.IsNullOrEmpty(permission.Id) || !Id.IsMatch(permission.Id) || string.IsNullOrWhiteSpace(permission.Label) || permission.Label.Length > 200 ||
                string.IsNullOrWhiteSpace(permission.Purpose) || permission.Purpose.Length > 1000 ||
                permission.Label.Any(char.IsControl) || permission.Purpose.Any(char.IsControl) ||
                permission.Capabilities.Count is < 1 or > 128 || permission.Capabilities.Distinct(StringComparer.Ordinal).Count() != permission.Capabilities.Count ||
                permission.ScopeSets.Distinct(StringComparer.Ordinal).Count() != permission.ScopeSets.Count ||
                permission.ScopeSets.Any(x => connection?.ScopeSets.Any(s => s.Id == x) != true))
                errors.Add("Permissions require exact declared capabilities, scopes and bounded labels.");
            foreach (var capability in permission.Capabilities)
            {
                var operation = manifest.ProviderOperations.FirstOrDefault(x => x.Capability == capability);
                if (operation?.Http is not { Bootstrap: false } http || http.Connection != a.Connection ||
                    http.ScopeSets.Except(permission.ScopeSets, StringComparer.Ordinal).Any())
                    errors.Add("A business permission can expose only normal operations covered by its declared scope sets.");
            }
        }
        if (a.Lifecycle is not { } life || life.AuthorizationCheckHours is < 1 or > 8760 || life.MetadataRefreshDays is < 1 or > 3650 ||
            life.DeletionMaximumDays is < 1 or > 365 || life.DeletionTargetHours is < 1 or > 8760 ||
            life.DeletionTargetHours > life.DeletionMaximumDays * 24 || life.RevocationScope is not ("authorization" or "subject-project"))
            errors.Add("Account lifecycle requires bounded validation, refresh and deletion intervals and a known revocation scope.");
        return errors;
    }

    private static bool Path(string value) => value is { Length: <= 256 } && Pointer.IsMatch(value);
    private static bool ValidPageToken(AgentProviderOperationDeclaration? operation, string field)
    {
        if (!Regex.IsMatch(field, @"\A[a-zA-Z][a-zA-Z0-9]{0,63}\z") || operation?.Http is not { } http ||
            !http.QueryInputs.Values.Contains("/" + field, StringComparer.Ordinal)) return false;
        var schema = operation.InputSchema;
        return schema.ValueKind == System.Text.Json.JsonValueKind.Object &&
            (!schema.TryGetProperty("required", out var required) ||
             required.ValueKind == System.Text.Json.JsonValueKind.Array && !required.EnumerateArray().Any(x => x.ValueKind == System.Text.Json.JsonValueKind.String && x.GetString() == field)) &&
            schema.TryGetProperty("properties", out var properties) && properties.ValueKind == System.Text.Json.JsonValueKind.Object &&
            properties.TryGetProperty(field, out var page) && page.ValueKind == System.Text.Json.JsonValueKind.Object &&
            page.TryGetProperty("type", out var type) && type.ValueKind == System.Text.Json.JsonValueKind.String && type.GetString() == "string" &&
            page.TryGetProperty("maxLength", out var max) && max.ValueKind == System.Text.Json.JsonValueKind.Number &&
            max.TryGetInt32(out var length) && length is >= 1 and <= 2048;
    }
    private static bool Https(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        !uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
        uri.IsDefaultPort && uri.Host.Contains('.') && Uri.CheckHostName(uri.Host) == UriHostNameType.Dns &&
        !uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
        !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
}
