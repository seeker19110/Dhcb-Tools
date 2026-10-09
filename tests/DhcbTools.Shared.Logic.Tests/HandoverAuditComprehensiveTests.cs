using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.AsBuilt;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public sealed class HandoverAuditComprehensiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dhcb-handover-audit-" + Guid.NewGuid().ToString("N"));

    public HandoverAuditComprehensiveTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CollectIncludesSheetIndexesFromEveryModel()
    {
        File.WriteAllText(Path.Combine(_root, "a.csv"), SheetIndexRow.ToCsv(new[] { Sheet("A-101") }));
        File.WriteAllText(Path.Combine(_root, "b.csv"), SheetIndexRow.ToCsv(new[] { Sheet("B-201") }));
        var input = new HandoverInput { OutputFolder = _root };
        HandoverPackage.Collect(input);
        Assert.Equal(new[] { "A-101", "B-201" }, input.Sheets.Select(s => s.Number));
    }

    [Theory]
    [InlineData("A-1", "A-10 plan.pdf")]
    [InlineData("A1", "BA1.pdf")]
    public void SimilarDrawingNumberCannotSatisfyMissingSheet(string number, string pdf)
    {
        var files = Files(pdf);
        var result = HandoverPackageValidator.ValidatePackage(_root, files, new[] { Sheet(number) });
        Assert.False(result.IsPassed);
        Assert.Contains(number, result.UnboundSheets);
    }

    [Theory]
    [InlineData("A-1", "Building_A-1_plan.pdf")]
    [InlineData("A1", "A1.pdf")]
    public void DrawingNumberWithSeparatorsStillBinds(string number, string pdf)
    {
        var result = HandoverPackageValidator.ValidatePackage(_root, Files(pdf), new[] { Sheet(number) });
        Assert.True(result.IsPassed);
    }

    [Theory]
    [InlineData("{\"success\":true,\"errors\":null}")]
    [InlineData("{\"success\":true,\"messages\":null}")]
    [InlineData("null")]
    public void NullLogPayloadCannotBecomeACompleteStep(string json) => Assert.Null(RunLog.Deserialize(json));

    [Theory]
    [InlineData("{\"groups\":null}")]
    [InlineData("{\"groups\":[null]}")]
    [InlineData("{\"groups\":[{\"items\":null}]}")]
    [InlineData("{\"groups\":[{\"items\":[null]}]}")]
    [InlineData("{\"groups\":[{\"items\":[{\"patterns\":null}]}]}")]
    [InlineData("{\"groups\":[{\"items\":[{\"patterns\":[null]}]}]}")]
    [InlineData("{\"groups\":[{\"items\":[{\"note\":null}]}]}")]
    public void InvalidDossierShapeProducesAnInputError(string json) => Assert.Throws<ArgumentException>(() => DossierSpec.FromJson(json));

    [Theory]
    [InlineData(false, false, false, "Thành công", true)]
    [InlineData(true, false, false, "Một phần", false)]
    [InlineData(false, true, false, "Lỗi", false)]
    [InlineData(false, false, true, "Bỏ qua", false)]
    public void HandoverShowsActualCompletion(bool partial, bool errors, bool skipped, string label, bool complete)
    {
        var entry = new RunLogEntry { Success = true, PartialSuccess = partial, Skipped = skipped };
        if (errors) entry.Errors.Add("E-TEST");
        var input = new HandoverInput();
        input.Entries.Add(entry);
        Assert.Contains(label, HandoverPackage.Html(input));
        var step = JObject.Parse(HandoverPackage.ToJson(input))["steps"]![0]!;
        Assert.Equal(complete, step["IsComplete"]!.Value<bool>());
        Assert.Equal(partial, step["PartialSuccess"]!.Value<bool>());
        Assert.Equal(errors ? 1 : 0, ((JArray)step["Errors"]!).Count);
    }

    [Fact]
    public void StrictLogReadKeepsEvidenceOfEveryInvalidNonblankLine()
    {
        var path = Path.Combine(_root, "run.jsonl");
        Assert.Empty(RunLog.ReadAll(path, reportInvalid: true));
        File.WriteAllText(path, "\n{broken\n" + RunLog.Serialize(new RunLogEntry { Success = true }) + "\nnull\n");
        var entries = RunLog.ReadAll(path, reportInvalid: true);
        Assert.Equal(3, entries.Count);
        Assert.False(entries[0].IsComplete);
        Assert.Contains("2", entries[0].Summary);
        Assert.True(entries[1].IsComplete);
        Assert.False(entries[2].IsComplete);
        Assert.Single(RunLog.ReadAll(path));
    }

    private List<HandoverFile> Files(string pdf)
    {
        return new[] { (pdf, "PDF"), ("model.ifc", "IFC"), ("index.csv", "CSV"), ("metadata.json", "JSON") }
            .Select(item =>
            {
                var path = Path.Combine(_root, item.Item1);
                File.WriteAllText(path, "fixture");
                return new HandoverFile(item.Item1, item.Item2, new FileInfo(path).Length, HandoverPackage.Sha256Of(path));
            }).ToList();
    }

    private static SheetIndexRow Sheet(string number) => new(number, "Plan", "", "", "", "", "", 1);
    public void Dispose() => Directory.Delete(_root, true);
}
