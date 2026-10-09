// API double for executing the linked Revit transaction/failure code on CI without Autodesk binaries.
// This verifies host adapter decisions; it is not evidence of Revit runtime acceptance.
using DhcbTools.Shared.Hosting;

namespace Autodesk.Revit.DB
{
    public enum TransactionStatus { Uninitialized, Started, Committed, RolledBack, Pending }
    public enum FailureSeverity { Warning, Error, DocumentCorruption }
    public enum FailureProcessingResult { Continue, ProceedWithCommit, ProceedWithRollBack }
    public enum FailureResolutionType { Default }
    public enum FamilySource { Family }
    public enum StorageType { Double, String, Integer }
    public sealed class Parameter
    {
        public StorageType StorageType { get; init; }
        public bool IsReadOnly { get; init; }
        public bool AcceptWrite { get; init; } = true;
        public double CurrentDouble { get; init; } = double.NaN;
        public string? CurrentString { get; init; }
        public object? Written { get; private set; }
        public double AsDouble() => Written is double value ? value : CurrentDouble;
        public string? AsString() => Written as string ?? CurrentString;
        public bool Set(double value) { if (AcceptWrite) Written = value; return AcceptWrite; }
        public bool Set(string value) { if (AcceptWrite) Written = value; return AcceptWrite; }
    }
    public sealed class Element
    {
        public Dictionary<string, Parameter> InstanceParameters { get; } = new();
        public Dictionary<string, Parameter> TypeParameters { get; } = new();
    }
    public interface IFamilyLoadOptions
    {
        bool OnFamilyFound(bool inUse, out bool overwrite);
        bool OnSharedFamilyFound(Family family, bool inUse, out FamilySource source, out bool overwrite);
    }
    public interface IFailuresPreprocessor { FailureProcessingResult PreprocessFailures(FailuresAccessor accessor); }
    public sealed class ElementId { public long Value { get; init; } }
    public sealed class FailureDefinitionId { public Guid Guid { get; init; } }
    public sealed class FailureMessageAccessor
    {
        public FailureSeverity Severity { get; init; }
        public bool Resolutions { get; init; }
        public int Attempts { get; init; }
        public string Text { get; init; } = "failure";
        public bool ThrowDescription { get; init; }
        public bool ThrowIds { get; init; }
        public List<ElementId> Ids { get; } = new();
        public FailureSeverity GetSeverity() => Severity;
        public bool HasResolutions() => Resolutions;
        public string GetDescriptionText() => ThrowDescription ? throw new InvalidOperationException("description") : Text;
        public FailureDefinitionId GetFailureDefinitionId() => new() { Guid = Guid.Empty };
        public ICollection<ElementId> GetFailingElementIds() => ThrowIds ? throw new InvalidOperationException("ids") : Ids;
    }
    public sealed class FailureHandlingOptions
    {
        public bool ForcedModal { get; private set; }
        public bool ClearAfterRollback { get; private set; }
        public FailureHandlingOptions SetForcedModalHandling(bool value) { ForcedModal = value; return this; }
        public FailureHandlingOptions SetClearAfterRollback(bool value) { ClearAfterRollback = value; return this; }
    }
    public sealed class FailuresAccessor
    {
        public List<FailureMessageAccessor> Failures { get; } = new();
        public int DeletedWarnings { get; private set; }
        public int ResolvedFailures { get; private set; }
        public FailureHandlingOptions Options { get; private set; } = new();
        public IList<FailureMessageAccessor> GetFailureMessages() => Failures;
        public IList<FailureResolutionType> GetAttemptedResolutionTypes(FailureMessageAccessor failure)
            => Enumerable.Repeat(FailureResolutionType.Default, failure.Attempts).ToList();
        public void DeleteWarning(FailureMessageAccessor failure) => DeletedWarnings++;
        public void ResolveFailure(FailureMessageAccessor failure) => ResolvedFailures++;
        public FailureHandlingOptions GetFailureHandlingOptions() => Options;
        public void SetFailureHandlingOptions(FailureHandlingOptions options) => Options = options;
    }
    public sealed class Family { public string Name { get; init; } = ""; }
    public sealed class Document
    {
        public List<Family> Families { get; } = new();
        public List<Transaction> Transactions { get; } = new();
        public List<TransactionGroup> Groups { get; } = new();
        public TransactionStatus CommitResult { get; set; } = TransactionStatus.Committed;
        public TransactionStatus? FinalStatus { get; set; }
        public Func<string, bool>? LoadResult { get; set; }
        public int LoadCalls { get; private set; }
        public bool LoadFamily(string path, IFamilyLoadOptions options, out Family? family)
        {
            LoadCalls++;
            if (LoadResult?.Invoke(path) == false) { family = null; return false; }
            family = new Family { Name = Path.GetFileNameWithoutExtension(path) };
            Families.Add(family);
            return true;
        }
    }
    public sealed class FilteredElementCollector
    {
        private readonly Document _document;
        public FilteredElementCollector(Document document) => _document = document;
        public FilteredElementCollector OfClass(Type type) => this;
        public IEnumerable<object> ToElements() => _document.Families;
    }
    public sealed class Transaction : IDisposable
    {
        private readonly Document _document;
        private readonly string _name;
        private List<Family>? _original;
        public Transaction(Document document, string name) { _document = document; _name = name; document.Transactions.Add(this); }
        public TransactionStatus Status { get; set; }
        public bool IsValidObject { get; set; } = true;
        public int RollbackCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public FailureHandlingOptions Options { get; private set; } = new();
        public string GetName() => _name;
        public TransactionStatus Start() { _original = _document.Families.ToList(); return Status = TransactionStatus.Started; }
        public TransactionStatus GetStatus() => IsValidObject ? Status : throw new InvalidOperationException("invalid native object");
        public FailureHandlingOptions GetFailureHandlingOptions() => Options;
        public void SetFailureHandlingOptions(FailureHandlingOptions options) => Options = options;
        public TransactionStatus Commit(FailureHandlingOptions options)
        {
            if (Status != TransactionStatus.Started) throw new InvalidOperationException("not Started");
            Options = options;
            Status = _document.FinalStatus ?? _document.CommitResult;
            if (Status == TransactionStatus.RolledBack) Restore();
            return _document.CommitResult;
        }
        public TransactionStatus RollBack()
        {
            if (Status != TransactionStatus.Started) throw new InvalidOperationException("illegal rollback");
            RollbackCalls++;
            Restore();
            return Status = TransactionStatus.RolledBack;
        }
        private void Restore() { _document.Families.Clear(); _document.Families.AddRange(_original ?? new()); }
        public void Dispose()
        {
            if (!IsValidObject) throw new InvalidOperationException("illegal invalid dispose");
            if (Status == TransactionStatus.Pending) throw new InvalidOperationException("illegal pending dispose");
            if (Status == TransactionStatus.Started) RollBack();
            DisposeCalls++;
        }
    }
    public sealed class TransactionGroup : IDisposable
    {
        private readonly Document _document;
        private readonly string _name;
        public TransactionGroup(Document document, string name) { _document = document; _name = name; document.Groups.Add(this); }
        public bool IsValidObject { get; set; } = true;
        public TransactionStatus Status { get; set; }
        public int DisposeCalls { get; private set; }
        public int RollbackCalls { get; private set; }
        public string GetName() => _name;
        public TransactionStatus GetStatus() => IsValidObject ? Status : throw new InvalidOperationException("invalid native group");
        public TransactionStatus Start() => Status = TransactionStatus.Started;
        public TransactionStatus Assimilate() { Status = _document.FinalStatus ?? _document.CommitResult; return _document.CommitResult; }
        public TransactionStatus RollBack()
        {
            if (_document.Transactions.Any(t => t.IsValidObject && t.Status == TransactionStatus.Pending)) throw new InvalidOperationException("pending child");
            RollbackCalls++;
            return Status = TransactionStatus.RolledBack;
        }
        public void Dispose()
        {
            if (!IsValidObject || Status == TransactionStatus.Pending) throw new InvalidOperationException("illegal group dispose");
            if (Status == TransactionStatus.Started) RollBack();
            DisposeCalls++;
        }
    }
    public sealed class SubTransaction
    {
        public TransactionStatus CommitResult { get; set; }
        public TransactionStatus FinalStatus { get; set; }
        public TransactionStatus Commit() => CommitResult;
        public TransactionStatus GetStatus() => FinalStatus;
    }
}

namespace DhcbTools.Core
{
    public interface ICoreCommand<in T> { string CommandName { get; } CommandResult Execute(Autodesk.Revit.DB.Document document, T config); }
    public static class RevitCompat
    {
        public static long IdValue(Autodesk.Revit.DB.ElementId id) => id.Value;
        public static Autodesk.Revit.DB.Parameter? LookupInstance(Autodesk.Revit.DB.Element element, string key, string? preferred)
            => element.InstanceParameters.TryGetValue(preferred ?? key, out var parameter) ? parameter : null;
        public static RevitTransaction StartTransaction(Autodesk.Revit.DB.Document doc, string name)
        {
            var transaction = new RevitTransaction(doc, name);
            transaction.Start();
            return transaction;
        }
    }
}
