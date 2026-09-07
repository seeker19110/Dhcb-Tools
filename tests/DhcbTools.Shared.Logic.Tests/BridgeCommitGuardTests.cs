using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public sealed class BridgeCommitGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dhcb-commit-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
    private long _revision;
    private int _writes;
    private readonly BridgeCommitGuard _guard;

    public BridgeCommitGuardTests()
    {
        Directory.CreateDirectory(_root);
        _guard = new BridgeCommitGuard(Path.Combine(_root, "ledger"), () => _now);
    }

    public void Dispose() => Directory.Delete(_root, true);

    private CommandResult Dispatch(BridgeRequest request)
    {
        if (request.Config!["dryRun"]!.Value<bool>()) return CommandResult.Ok("Preview", 2);
        _writes++;
        _revision++;
        return CommandResult.Ok("Committed", 2).WithChanged(42);
    }

    private CommandResult Execute(BridgeRequest request, string model = "A") =>
        _guard.Execute("revit", request, model, () => _revision, Dispatch);

    private static BridgeRequest Request(bool dry = true, JObject? config = null) => new()
    {
        Command = "AutoNumbering",
        Config = config ?? new JObject { ["prefix"] = "D-", ["dryRun"] = dry },
    };

    private BridgeRequest Approved(JObject? config = null)
    {
        var request = Request(config: config);
        var preview = Execute(request);
        Assert.True(preview.Success, preview.Summary);
        Assert.NotNull(preview.PreviewToken);
        request.PreviewToken = preview.PreviewToken;
        request.DocumentId = preview.DocumentId;
        request.Config!["dryRun"] = false;
        return request;
    }

    [Fact]
    public void Preview_binds_context_and_replay_survives_restart_and_model_change()
    {
        var request = Approved();
        Assert.Equal("A", request.DocumentId);
        var result = Execute(request);
        Assert.True(result.Success);
        Assert.Equal(new long[] { 42 }, result.ChangedIds);
        Assert.True(Execute(request, "B").Success);
        var restarted = new BridgeCommitGuard(Path.Combine(_root, "ledger"));
        var replay = restarted.Execute("revit", request, "new-session", () => 999, _ => throw new Exception("must not dispatch"));
        Assert.True(replay.Success);
        Assert.Equal(result.ChangedIds, replay.ChangedIds);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public void Lost_preview_after_restart_cannot_authorize_new_write()
    {
        var request = Approved();
        var restarted = new BridgeCommitGuard(Path.Combine(_root, "ledger"));
        Assert.Contains("E-PREVIEW-INVALID", restarted.Execute("revit", request, "A", () => 0, Dispatch).Summary);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void Parallel_retries_dispatch_only_once()
    {
        var request = Approved();
        var results = Enumerable.Range(0, 12).AsParallel().Select(_ => Execute(request)).ToArray();
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(1, _writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../other")]
    public void Commit_requires_valid_token(string? token)
    {
        var request = Request(false);
        request.DocumentId = "A";
        request.PreviewToken = token;
        Assert.Contains("E-PREVIEW-REQUIRED", Execute(request).Summary);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void Commit_requires_original_document_and_rejects_unknown_token()
    {
        var request = Approved();
        request.DocumentId = null;
        Assert.Contains("E-PREVIEW-REQUIRED", Execute(request).Summary);
        request.DocumentId = "A";
        request.PreviewToken = Guid.NewGuid().ToString("N");
        Assert.Contains("E-PREVIEW-INVALID", Execute(request).Summary);
    }

    [Fact]
    public void Different_model_revision_config_and_expiry_reject_writes()
    {
        var request = Approved();
        Assert.Contains("E-DOCUMENT-CHANGED", Execute(request, "B").Summary);
        request.Config!["prefix"] = "X-";
        Assert.Contains("E-PREVIEW-INVALID", Execute(request).Summary);
        request.Config["prefix"] = "D-";
        _revision++;
        Assert.Contains("E-PREVIEW-CHANGED", Execute(request).Summary);
        request = Approved();
        _now += _guard.Lifetime;
        Assert.Contains("E-PREVIEW-INVALID", Execute(request).Summary);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void Reordered_json_is_equivalent_but_values_are_not()
    {
        var request = Approved(JObject.Parse("{'dryRun':true,'levels':[{'name':'L1','height':2}],'prefix':'D-'}"));
        request.Config = JObject.Parse("{'prefix':'D-','levels':[{'height':2,'name':'L1'}],'dryRun':false}");
        Assert.True(Execute(request).Success);
        request.Config["prefix"] = "X-";
        Assert.Contains("E-PREVIEW-INVALID", Execute(request).Summary);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public void Defaults_are_preview_and_request_is_not_mutated()
    {
        var req = Request();
        req.Config = null;
        var result = Execute(req);
        Assert.True(result.Success);
        Assert.Null(req.Config);
        Assert.Equal(_now.AddMinutes(10), result.PreviewExpiresUtc);
        Assert.Contains("bridge-commits", BridgeCommitGuard.DefaultDirectory("revit"));
        Assert.Equal(0, _writes);
    }

    [Theory]
    [InlineData("NotACommand")]
    [InlineData("RunTests")]
    public void Nonpublic_commands_cannot_bypass_guard(string name)
    {
        var request = Request();
        request.Command = name;
        Assert.Contains("E-PREVIEW-INVALID", Execute(request).Summary);
    }

    [Theory]
    [InlineData("{'dryRun':'false'}")]
    [InlineData("{'dryRun':null}")]
    [InlineData("{'dryRun':1}")]
    [InlineData("{'dryRun':true,'DryRun':false}")]
    public void Ambiguous_dry_run_is_rejected(string config)
    {
        Assert.Contains("E-PREVIEW-INVALID", Execute(Request(config: JObject.Parse(config))).Summary);
    }

    [Fact]
    public void Read_commands_do_not_require_preview_token()
    {
        var request = Request();
        request.Command = "HealthReport";
        var result = Execute(request);
        Assert.True(result.Success);
        Assert.Null(result.PreviewToken);
    }

    [Theory]
    [InlineData("ClashDetection")]
    [InlineData("ParameterRuleCheck")]
    [InlineData("ConnectorChecker")]
    public void Optional_view_creation_also_requires_preview(string command)
    {
        var req = Request(false, new JObject { ["create3dView"] = true, ["dryRun"] = false });
        req.Command = command;
        Assert.Contains("E-PREVIEW-REQUIRED", Execute(req).Summary);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void Failed_partial_or_error_preview_never_issues_token()
    {
        foreach (var result in new[] { CommandResult.Fail("bad"), new CommandResult { Success = true, PartialSuccess = true },
                     CommandResult.Ok("bad") })
        {
            result.Errors.Add("validation error");
            var returned = _guard.Execute("revit", Request(), "A", () => 0, _ => result);
            Assert.Null(returned.PreviewToken);
        }
    }

    [Fact]
    public void Preview_that_changes_model_is_not_authorized()
    {
        var result = _guard.Execute("revit", Request(), "A", () => _revision, _ =>
        {
            _revision++;
            return CommandResult.Ok("Preview");
        });
        Assert.Contains("E-PREVIEW-CHANGED", result.Summary);
    }

    [Fact]
    public void Capacity_is_bounded_and_expired_entries_are_reclaimed()
    {
        _guard.MaxPlans = 1;
        Assert.True(Execute(Request()).Success);
        Assert.Contains("E-PREVIEW-CAPACITY", Execute(Request()).Summary);
        _now += TimeSpan.FromMinutes(11);
        Assert.True(Execute(Request()).Success);
    }

    [Fact]
    public void File_contents_are_bound_even_with_same_length_and_timestamp()
    {
        var file = Path.Combine(_root, "input.csv");
        File.WriteAllText(file, "A,1");
        var stamp = File.GetLastWriteTimeUtc(file);
        var request = Approved(new JObject { ["inputPath"] = file });
        File.WriteAllText(file, "B,2");
        File.SetLastWriteTimeUtc(file, stamp);
        Assert.Contains("E-PREVIEW-CHANGED", Execute(request).Summary);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void Folder_contents_and_membership_are_bound_recursively()
    {
        var folder = Path.Combine(_root, "families");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllText(Path.Combine(folder, "nested", "a.rfa"), "A");
        var request = Approved(new JObject { ["familyFolder"] = folder });
        File.WriteAllText(Path.Combine(folder, "new.rfa"), "B");
        Assert.Contains("E-PREVIEW-CHANGED", Execute(request).Summary);
    }

    [Fact]
    public void Unchanged_paths_and_output_fields_allow_commit()
    {
        var file = Path.Combine(_root, "template.rte");
        File.WriteAllText(file, "template");
        var request = Approved(new JObject { ["templatePath"] = file, ["outputPath"] = "ignored",
            ["reportPath"] = "ignored", ["optionalPath"] = JValue.CreateNull(), ["blankPath"] = "" });
        Assert.True(Execute(request).Success);
    }

    [Fact]
    public void Invalid_path_or_linked_directory_fails_closed()
    {
        Assert.Contains("E-COMMIT-UNKNOWN", Execute(Request(config: new JObject { ["inputPath"] = 3 })).Summary);
        var folder = Path.Combine(_root, "real");
        Directory.CreateDirectory(folder);
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, folder);
        Assert.Contains("E-COMMIT-UNKNOWN", Execute(Request(config: new JObject { ["familyFolder"] = link })).Summary);
    }

    [Fact]
    public void Oversized_input_directory_fails_closed()
    {
        var folder = Path.Combine(_root, "many");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 4096; i++) File.WriteAllText(Path.Combine(folder, i + ".rfa"), "");
        Assert.Contains("E-COMMIT-UNKNOWN", Execute(Request(config: new JObject { ["familyFolder"] = folder })).Summary);
    }

    [Fact]
    public void Dispatch_crash_leaves_durable_claim_and_never_reexecutes()
    {
        var request = Approved();
        var result = _guard.Execute("revit", request, "A", () => 0, _ =>
        {
            _writes++;
            throw new IOException("crash after mutation");
        });
        Assert.Contains("E-COMMIT-UNKNOWN", result.Summary);
        var restarted = new BridgeCommitGuard(Path.Combine(_root, "ledger"));
        Assert.Contains("E-COMMIT-UNKNOWN", restarted.Execute("revit", request, "A", () => 0, Dispatch).Summary);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public void Cannot_persist_claim_means_no_dispatch()
    {
        var request = Approved();
        File.WriteAllText(Path.Combine(_root, "ledger"), "not a directory");
        Assert.Contains("E-COMMIT-UNKNOWN", Execute(request).Summary);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void Claim_created_by_competing_instance_cannot_be_overwritten()
    {
        var request = Approved();
        Directory.CreateDirectory(Path.Combine(_root, "ledger"));
        var result = _guard.Execute("revit", request, "A", () =>
        {
            File.WriteAllText(Path.Combine(_root, "ledger", request.PreviewToken + ".claim"), "other-request");
            return 0;
        }, Dispatch);
        Assert.Contains("E-PREVIEW-INVALID", result.Summary);
        Assert.Equal(0, _writes);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("truncated-json")]
    public void Corrupted_result_cannot_cause_reexecution(string content)
    {
        var request = Approved();
        Assert.True(Execute(request).Success);
        File.WriteAllText(Path.Combine(_root, "ledger", request.PreviewToken + ".claim.result"), content);
        Assert.Contains("E-COMMIT-UNKNOWN", Execute(request).Summary);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public void Document_revision_is_per_session_and_counts_changes()
    {
        var a = new object();
        var b = new object();
        Assert.Equal(0, BridgeDocumentContext.RevisionFor(a));
        BridgeDocumentContext.Touch(a);
        BridgeDocumentContext.Touch(b);
        Assert.Equal(1, BridgeDocumentContext.RevisionFor(a));
        Assert.Equal(1, BridgeDocumentContext.RevisionFor(b));
        Assert.NotEqual(BridgeDocumentContext.IdFor(a), BridgeDocumentContext.IdFor(b));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_preview_and_commit_carry_metadata_in_sync_and_background(bool background)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var server = new HttpBridgeServer(port, "revit", "test")
        {
            ExecuteAsync = item =>
            {
                if (item.TryClaim()) item.Completion.SetResult(Execute(item.Request));
                return Task.CompletedTask;
            },
        };
        server.Start(Path.Combine(_root, "http-token"));
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", server.Token);
        async Task<JObject> Send(JObject payload)
        {
            using var response = await http.PostAsync("/execute", new StringContent(
                payload.ToString(), System.Text.Encoding.UTF8, "application/json"));
            var result = JObject.Parse(await response.Content.ReadAsStringAsync());
            if (background)
            {
                var id = result["id"]!.Value<string>();
                for (var i = 0; i < 100; i++)
                {
                    var state = JObject.Parse(await http.GetStringAsync("/progress/" + id));
                    if (state["status"]!.Value<string>() == "done") return (JObject)state["result"]!;
                    await Task.Delay(10);
                }
                throw new Exception("Background result did not complete");
            }
            return result;
        }
        var payload = JObject.Parse("{'command':'AutoNumbering','config':{'dryRun':true}}");
        payload["async"] = background;
        var preview = await Send(payload);
        Assert.NotNull(preview["previewToken"]!.Value<string>());
        Assert.Equal("A", preview["documentId"]!.Value<string>());
        Assert.NotNull(preview["previewExpiresUtc"]!.Value<DateTime?>());
        payload["previewToken"] = preview["previewToken"]!.DeepClone();
        payload["documentId"] = preview["documentId"]!.DeepClone();
        payload["config"]!["dryRun"] = false;
        Assert.True((await Send(payload))["success"]!.Value<bool>());
        Assert.True((await Send(payload))["success"]!.Value<bool>());
        Assert.Equal(1, _writes);
    }
}
