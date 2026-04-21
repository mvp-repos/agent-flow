using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using AgentFlow.Core.Abstractions;
using AgentFlow.Core.Models;
using AgentFlow.Shared.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AgentFlow.Shared.Helpers;

namespace AgentFlow.Adapters.AzureDevOps
{
    /// <summary>
    /// Azure DevOps implementation of <see cref="IWorkItemProvider"/> that uses the Azure DevOps
    /// REST API to fetch work items, add comments, update state, and detect approval signals.
    /// </summary>
    public sealed class AzureDevOpsWorkItemProvider : IWorkItemProvider
    {
        private const string WitApiVersion      = "7.0";
        private const string CommentsApiVersion = "7.0-preview.3";

        // Services
        private readonly HttpClient        _http;
        private readonly AzureDevOpsConfig _cfg;

        public AzureDevOpsWorkItemProvider(HttpClient http, IOptions<AzureDevOpsConfig> cfg)
        {
            _http = http;
            _cfg  = cfg.Value;
            ConfigureAuthentication();
        }

        /// <summary>
        /// Configures the HttpClient.
        /// </summary>
        private void ConfigureAuthentication()
        {
            if (string.IsNullOrEmpty(_cfg.PersonalAccessToken))
                return;

            var bytes = Encoding.ASCII.GetBytes(":" + _cfg.PersonalAccessToken);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }

        private string GetWorkItemsBaseUrl() =>
            $"{_cfg.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(_cfg.Project)}/_apis/wit";

        private string GetGitBaseUrl() =>
            $"{_cfg.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(_cfg.Project)}/_apis/git";

        /// <summary>
        /// Builds a proper download URL for an attachment so the server returns the correct content type and filename.
        /// Relation URLs from the work item often lack api-version and fileName, which can cause wrong content type or "unknown" file type.
        /// </summary>
        /// <param name="attachmentId">The attachment ID extracted from the relation URL.</param>
        /// <param name="fileName">The attachment file name, used for the fileName query parameter to ensure correct content type and name on download.</param>
        /// <returns> 
        /// A URL that can be used to download the attachment with correct content type and filename.
        /// </returns>
        private string BuildAttachmentDownloadUrl(string attachmentId, string fileName)
        {
            var encodedName = Uri.EscapeDataString(fileName);
            return $"{GetWorkItemsBaseUrl()}/attachments/{Uri.EscapeDataString(attachmentId)}?api-version={WitApiVersion}&fileName={encodedName}&download=true";
        }

        /// <inheritdoc />
        public async Task<WorkItem> GetWorkItemAsync(string workItemId, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workItemId))
                    throw new ArgumentException("Work item ID cannot be null or empty.", nameof(workItemId));

                // Use the Azure DevOps REST API to get the work item details, including all fields
                var url            = $"{GetWorkItemsBaseUrl()}/workitems/{Uri.EscapeDataString(workItemId)}?$expand=all&api-version={WitApiVersion}";
                using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json       = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var root       = JObject.Parse(json);
                var id         = root["id"]?.ToString() ?? workItemId;
                var fields     = root["fields"] as JObject;
                var fieldsDict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                if (fields != null)
                {
                    foreach (var p in fields.Properties())
                        fieldsDict[p.Name] = p.Value?.ToString();
                }
                var title = fieldsDict.GetValueOrDefault("System.Title");
                var desc  = fieldsDict.GetValueOrDefault("System.Description");
                if (string.IsNullOrEmpty(title))
                    title = id;

                // Parse relations for AttachedFile to expose attachment metadata (e.g. for Cursor doability checks)
                var attachments = new List<WorkItemAttachment>();
                var relations   = root["relations"] as JArray;
                if (relations != null)
                {
                    foreach (var relToken in relations)
                    {
                        var rel = relToken["rel"]?.ToString();
                        if (!string.Equals(rel, "AttachedFile", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var relUrl = relToken["url"]?.ToString();
                        if (string.IsNullOrEmpty(relUrl))
                            continue;
                        var attachmentId   = relUrl.TrimEnd('/').Split('/').LastOrDefault() ?? relUrl;
                        var attachmentName = relToken["attributes"]?["name"]?.ToString() ?? attachmentId;

                        // Use a proper download URL (api-version + fileName + download=true) so the file type and name are correct
                        var downloadUrl    = BuildAttachmentDownloadUrl(attachmentId, attachmentName);
                        attachments.Add(new WorkItemAttachment(Id: attachmentId, Name: attachmentName, Url: downloadUrl));
                    }
                }

                return new WorkItem(Id: id, Title: title, Description: desc, Fields: fieldsDict, Attachments: attachments);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get work item {workItemId}.", ex);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<WorkItemComment>> GetCommentsAsync(string workItemId, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workItemId))
                    throw new ArgumentException("Work item ID cannot be null or empty.", nameof(workItemId));

                var url = $"{GetWorkItemsBaseUrl()}/workItems/{Uri.EscapeDataString(workItemId)}/comments?api-version={CommentsApiVersion}&$top=100";
                using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return Array.Empty<WorkItemComment>();

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var root = JObject.Parse(json);
                var arr  = root["comments"] as JArray;
                if (arr == null || arr.Count == 0)
                    return Array.Empty<WorkItemComment>();

                var list = new List<WorkItemComment>();
                foreach (var c in arr)
                {
                    var isDeleted = c["isDeleted"]?.Value<bool>() ?? true;
                    if (isDeleted) continue;
                    var text   = c["text"]?.ToString() ?? string.Empty;
                    var author = c["createdBy"]?["displayName"]?.ToString() ?? "Unknown";
                    var date   = c["createdDate"]?.ToString() ?? string.Empty;
                    list.Add(new WorkItemComment(Author: author, CreatedAt: date, Text: text));
                }

                return list;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Array.Empty<WorkItemComment>();
            }
        }

        /// <inheritdoc />
        public async Task AddCommentAsync(string workItemId, string commentMarkdown, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workItemId))
                    throw new ArgumentException("Work item ID cannot be null or empty.", nameof(workItemId));

                // Use the Azure DevOps REST API to add a comment to the work item. The comment is provided in markdown format
                var url            = $"{GetWorkItemsBaseUrl()}/workItems/{Uri.EscapeDataString(workItemId)}/comments?api-version={CommentsApiVersion}";
                var body           = new { text = commentMarkdown };
                var content        = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to add comment to work item {workItemId}.", ex);
            }
        }

        /// <inheritdoc />
        public async Task UpdateStateAsync(string workItemId, string newState, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workItemId))
                    throw new ArgumentException("Work item ID cannot be null or empty.", nameof(workItemId));

                // Use the Azure DevOps REST API to update the state of the work item. This typically involves
                // sending a PATCH request with the new state in the fields
                var url            = $"{GetWorkItemsBaseUrl()}/workitems/{Uri.EscapeDataString(workItemId)}?api-version={WitApiVersion}";
                var patch          = new[] { new { op = "add", path = "/fields/System.State", value = newState } };
                var content        = new StringContent(JsonConvert.SerializeObject(patch), Encoding.UTF8, "application/json-patch+json");
                using var response = await _http.PatchAsync(url, content, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update state of work item {workItemId}.", ex);
            }
        }

        /// <inheritdoc />
        public async Task RemoveTagAsync(string workItemId, string tagToRemove, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workItemId))
                    throw new ArgumentException("Work item ID cannot be null or empty.", nameof(workItemId));
                if (string.IsNullOrWhiteSpace(tagToRemove))
                    return;

                var wi      = await GetWorkItemAsync(workItemId, ct).ConfigureAwait(false);
                var tags    = wi.Fields.GetValueOrDefault("System.Tags") ?? string.Empty;
                var tagList = tags.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
                var removed = tagList.RemoveAll(t => string.Equals(t, tagToRemove.Trim(), StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                    return;

                var newTags = string.Join("; ", tagList);
                var url     = $"{GetWorkItemsBaseUrl()}/workitems/{Uri.EscapeDataString(workItemId)}?api-version={WitApiVersion}";
                var patch   = new[] { new { op = "replace", path = "/fields/System.Tags", value = newTags } };
                var content = new StringContent(JsonConvert.SerializeObject(patch), Encoding.UTF8, "application/json-patch+json");
                using var response = await _http.PatchAsync(url, content, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove tag from work item {workItemId}.", ex);
            }
        }

        /// <inheritdoc />
        public async Task<string> CreatePullRequestAsync(string repoName, string sourceBranch, string targetBranch, string title, string description, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(repoName))
                    throw new ArgumentException("Repository name is required.", nameof(repoName));
                if (string.IsNullOrWhiteSpace(sourceBranch))
                    throw new ArgumentException("Source branch is required.", nameof(sourceBranch));
                if (string.IsNullOrWhiteSpace(targetBranch))
                    throw new ArgumentException("Target branch is required.", nameof(targetBranch));

                // Repo ID, source ref, and target ref are required to create a pull request via the Azure DevOps API
                var repoId    = await ResolveRepositoryIdAsync(repoName.Trim(), ct).ConfigureAwait(false);
                var sourceRef = GitFx.NormalizeToHeadsRef(sourceBranch.Trim());
                var targetRef = await ResolveTargetRefAsync(repoId, targetBranch.Trim(), ct).ConfigureAwait(false);

                // The url for creating a pull request
                var url = $"{GetGitBaseUrl()}/repositories/{Uri.EscapeDataString(repoId)}/pullrequests?api-version=7.0";
                var payload = new
                {
                    sourceRefName = sourceRef,
                    targetRefName = targetRef,
                    title         = title ?? string.Empty,
                    description   = description ?? string.Empty
                };

                // Post the pull request creation request to the Azure DevOps API
                var content        = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var root = JObject.Parse(json);
                var web  = root["_links"]?["web"]?["href"]?.ToString();
                if (!string.IsNullOrWhiteSpace(web))
                    return web!;

                // Fallback: return REST URL if web link is unavailable.
                var prId = root["pullRequestId"]?.ToString();
                return string.IsNullOrWhiteSpace(prId) ? url : $"{url}&pullRequestId={prId}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to create pull request via Azure DevOps API.", ex);
            }
        }

        /// <summary>
        /// Resolves the Azure DevOps Git repository id (GUID) from a human-friendly repository name.
        /// This is used when creating pull requests because the PR API requires a repository id.
        /// </summary>
        /// <param name="repoName">The repository name as used in ADO URLs (e.g. <c>MyRepo</c>).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The repository id string returned by the ADO API.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="repoName"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the repository id cannot be resolved from the API response.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        private async Task<string> ResolveRepositoryIdAsync(string repoName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(repoName))
                throw new ArgumentException("Repository name cannot be null or empty.", nameof(repoName));

            // The ADO Git repository API allows fetching repository details by name, which includes the id needed for PR creation
            var url            = $"{GetGitBaseUrl()}/repositories/{Uri.EscapeDataString(repoName)}?api-version=7.0";
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var root = JObject.Parse(json);
            var id   = root["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"Could not resolve repository id for '{repoName}'.");
            return id!;
        }

        /// <summary>
        /// Resolves a target branch input into a concrete ref for pull request creation.
        /// Supports full refs (returned as-is), plain branch names (normalized to <c>refs/heads/...</c>),
        /// and simple wildcard patterns like <c>release/*</c> by selecting the latest matching branch by semantic version when possible.
        /// </summary>
        /// <param name="repoId">The repository id (GUID) used by ADO Git APIs.</param>
        /// <param name="targetBranch">Target branch name, ref, or wildcard pattern (e.g. <c>main</c>, <c>refs/heads/main</c>, <c>release/*</c>).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A full ref name (e.g. <c>refs/heads/main</c>).
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="repoId"/> or <paramref name="targetBranch"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a wildcard pattern matches no branches or cannot be resolved.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        private async Task<string> ResolveTargetRefAsync(string repoId, string targetBranch, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(repoId))
                throw new ArgumentException("Repository id cannot be null or empty.", nameof(repoId));
            if (string.IsNullOrWhiteSpace(targetBranch))
                throw new ArgumentException("Target branch cannot be null or empty.", nameof(targetBranch));

            // If already a full ref, return as-is.
            if (targetBranch.StartsWith("refs/", StringComparison.OrdinalIgnoreCase))
                return targetBranch;

            // Support simple wildcard patterns like release/* by resolving to the latest matching heads ref.
            if (targetBranch.Contains('*'))
            {
                var prefix = targetBranch.Split('*')[0].TrimEnd('/');
                if (string.IsNullOrWhiteSpace(prefix))
                    throw new InvalidOperationException($"Unsupported target branch pattern: '{targetBranch}'.");

                var filter         = $"heads/{prefix.TrimStart('/')}/";
                var refsUrl        = $"{GetGitBaseUrl()}/repositories/{Uri.EscapeDataString(repoId)}/refs?filter={Uri.EscapeDataString(filter)}&api-version=7.0";
                using var response = await _http.GetAsync(refsUrl, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var root = JObject.Parse(json);
                var values = root["value"] as JArray;
                if (values == null || values.Count == 0)
                    throw new InvalidOperationException($"Target branch pattern '{targetBranch}' matched no branches in the repository.");

                // Choose the highest semantic version suffix after the prefix when possible; otherwise fall back to last lexicographic.
                (Version? ver, string name) best = (null, string.Empty);
                foreach (var v in values)
                {
                    var name = v["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) 
                        continue;

                    // name is like refs/heads/release/1.2.3
                    var suffix = name!.Replace("refs/heads/", "", StringComparison.OrdinalIgnoreCase);
                    suffix     = suffix.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ? suffix.Substring(prefix.Length + 1) : suffix;
                    suffix     = suffix.TrimStart('v', 'V');
                    if (Version.TryParse(suffix, out var parsed))
                    {
                        if (best.ver == null || parsed > best.ver)
                            best = (parsed, name!);
                    }
                    else if (best.ver == null && string.Compare(name, best.name, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        best = (null, name!);
                    }
                }

                if (string.IsNullOrWhiteSpace(best.name))
                    throw new InvalidOperationException($"Target branch pattern '{targetBranch}' matched branches but none could be selected.");

                return best.name;
            }

            return GitFx.NormalizeToHeadsRef(targetBranch);
        }

        /// <inheritdoc />
        public async Task<ApprovalOutcome> GetWorkItemSignalAsync(
            string workItemId,
            CancellationToken ct,
            string? continueApprovalTag = null,
            string? regeneratePrdTag = null,
            string? regenerateCodeTag = null,
            bool regenerateCodeExclusive = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workItemId))
                    throw new ArgumentException("Work item ID cannot be null or empty.", nameof(workItemId));

                var wi      = await GetWorkItemAsync(workItemId, ct).ConfigureAwait(false);
                var tags    = wi.Fields.GetValueOrDefault("System.Tags") ?? string.Empty;
                // ADO stores tags as "a; b; c" — normalize to trimmed tokens for case-insensitive checks
                var tagList = tags.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();

                var stopTag = _cfg.StopTag?.Trim() ?? "agent:stop";

                // Stop wins so the workflow halts even if both "continue" and "regenerate" tags are present
                if (tagList.Any(t => string.Equals(t, stopTag, StringComparison.OrdinalIgnoreCase)))
                    return ApprovalOutcome.Stopped;

                // Exclusive code-gen mode: only stop (above) and regenerate-code are evaluated (prevents accidental continue/approve while waiting for answers).
                if (regenerateCodeExclusive && !string.IsNullOrWhiteSpace(regenerateCodeTag))
                {
                    var codeRegenTag = regenerateCodeTag.Trim();
                    if (tagList.Any(t => string.Equals(t, codeRegenTag, StringComparison.OrdinalIgnoreCase)))
                        return ApprovalOutcome.RegenerateCode;
                    return ApprovalOutcome.Pending;
                }

                // Inclusive regenerate-code: allow requesting another iteration during diff review (wins over Approved if both are set).
                if (!string.IsNullOrWhiteSpace(regenerateCodeTag))
                {
                    var codeRegenTag = regenerateCodeTag.Trim();
                    if (tagList.Any(t => string.Equals(t, codeRegenTag, StringComparison.OrdinalIgnoreCase)))
                        return ApprovalOutcome.RegenerateCode;
                }

                // PRD re-run is explicit: caller passes the tag to detect (e.g. agent:regenerate-prd)
                if (!string.IsNullOrWhiteSpace(regeneratePrdTag))
                {
                    var regenPrdTagTrimmed = regeneratePrdTag.Trim();
                    if (tagList.Any(t => string.Equals(t, regenPrdTagTrimmed, StringComparison.OrdinalIgnoreCase)))
                        return ApprovalOutcome.RegeneratePrd;
                }

                // Tag that means Approved for this poll: PRD gate passes PrdApprovalTag; final gate uses configured ApprovalTag (default agent:approved)
                var approvalTagToUse = continueApprovalTag?.Trim() ?? _cfg.ApprovalTag?.Trim() ?? "agent:approved";
                if (tagList.Any(t => string.Equals(t, approvalTagToUse, StringComparison.OrdinalIgnoreCase)))
                    return ApprovalOutcome.Approved;

                return ApprovalOutcome.Pending;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to check approval for work item {workItemId}.", ex);
            }
        }
    }
}
