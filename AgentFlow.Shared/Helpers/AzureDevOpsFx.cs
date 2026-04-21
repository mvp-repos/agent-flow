using Newtonsoft.Json.Linq;

namespace AgentFlow.Shared.Helpers;

/// <summary>
/// Shared helpers for parsing Azure DevOps identity values returned as JSON (e.g. System.AssignedTo).
/// </summary>
public static class AzureDevOpsFx
{
    /// <summary>
    /// Extracts the display name (or unique name) from an ADO identity field value.
    /// ADO can represent identity fields as JSON objects containing <c>displayName</c> and <c>uniqueName</c>.
    /// </summary>
    /// <param name="raw">Raw field value (plain string or JSON object string from ADO).</param>
    /// <returns>
    /// Display name, unique name, or the original string if not JSON; empty string if input is blank.
    /// </returns>
    public static string GetDisplayNameOrUniqueName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        try
        {
            var token = JToken.Parse(raw);
            if (token is JObject obj)
            {
                var displayName = obj["displayName"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(displayName))
                    return displayName;
                var uniqueName = obj["uniqueName"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(uniqueName))
                    return uniqueName;
            }
        }
        catch
        {
            // Not JSON; use as-is
        }

        return raw.Trim();
    }
}

