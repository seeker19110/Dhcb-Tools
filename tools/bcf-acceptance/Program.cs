using DhcbTools.Shared.Logic.Bcf;
using DhcbTools.Shared.Logic.Checks;
using System.Text.Json;

if (args.Length != 1) throw new ArgumentException("Pass a fresh output directory.");
var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Output directory must be empty; existing evidence is preserved.");
Directory.CreateDirectory(output);
var evidence = new List<object>();

void Write(string name, IEnumerable<BcfIssue> issues, BcfProject? project)
{
    var list = issues.ToList();
    BcfWriter.WriteFile(Path.Combine(output, name + ".bcf"), list, project);
    evidence.Add(new { file = name + ".bcf", topics = list.Select(i => new {
        guid = i.Guid, title = i.Title, labels = i.Labels, stage = i.Stage,
        target = i.Target == null ? null : new[] { i.Target.X, i.Target.Y, i.Target.Z },
        authoringIds = i.Components.Select(c => c.AuthoringToolId),
    }) });
}

var records = new[] {
    new ClashRecord(101, "Ducts", "Hard", 201, "Wall", 1000, 2000, 3000, "hard", null,
        ClashClassifier.Classify(101, "Ducts", 201, "Wall", 1000, 2000, 3000, 1000000, 0, penetrationDepthMm: 100)),
    new ClashRecord(102, "Ducts", "Soft", 202, "Wall", 4000, 5000, 6000, "soft", "ARC-Link",
        ClashClassifier.Classify(102, "Ducts", 202, "Wall", 4000, 5000, 6000, 0, 50)),
    new ClashRecord(103, "Ducts", "Tolerance", 203, "Wall", 7000, 8000, 9000, "tolerance", null,
        ClashClassifier.Classify(103, "Ducts", 203, "Wall", 7000, 8000, 9000, 1, 0, penetrationDepthMm: 1)),
};
var classified = ClashReport.BcfIssues("QA & phối hợp <MEP>", records);
foreach (var issue in classified) {
    issue.Stage = "Thi công & điều phối";
    issue.Labels.Add(" Phối hợp & <MEP> ");
}
Write("classified", classified, new BcfProject { Name = "QA & phối hợp <MEP>" });
Write("empty", Array.Empty<BcfIssue>(), null);
Write("default-project", new[] { new BcfIssue(BcfWriter.GuidFromKey("default"), "Default project") { Target = new BcfPoint(0, 0, 0) } }, null);
Write("no-camera", new[] { new BcfIssue(BcfWriter.GuidFromKey("no-camera"), "No camera") }, null);
Write("vertical-camera", new[] { new BcfIssue(BcfWriter.GuidFromKey("vertical"), "Vertical camera") {
    Target = new BcfPoint(-5, 2, 12), ViewDirection = new BcfPoint(0, 0, -1), ViewDistance = 7 } }, null);
File.WriteAllText(Path.Combine(output, "expected.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Generated 5 BCF fixtures: " + output);
