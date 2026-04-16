using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

// Legacy audit:
// Primary legacy source: tools/fortran-debug/json_trace.f event schema and focused trace workflow
// Secondary legacy source: tests/XFoil.Core.Tests/FortranParity/ParityTraceLoader.cs
// Role in port: Managed-only live comparator that can stop a managed parity run at the first mismatching structured trace event.
// Differences: Classic XFoil had no cross-runtime live compare loop; this helper consumes stored Fortran traces and compares them against in-process managed events.
// Decision: Keep the comparator in the test harness because it is parity debugging infrastructure rather than solver logic.
namespace XFoil.Core.Tests.FortranParity;

internal sealed class LiveParityTraceMismatchException : Exception
{
    public LiveParityTraceMismatchException(
        string message,
        ParityTraceRecord? referenceRecord = null,
        ParityTraceRecord? managedRecord = null,
        IReadOnlyList<LiveParityMatchedEvent>? matchedContext = null,
        IReadOnlyList<LiveParityManagedFrame>? managedCallStack = null,
        IReadOnlyList<ParityTraceRecord>? recentManagedEvents = null,
        int comparedComparableEvents = 0)
        : base(message)
    {
        ReferenceRecord = referenceRecord;
        ManagedRecord = managedRecord;
        MatchedContext = matchedContext?.ToArray() ?? Array.Empty<LiveParityMatchedEvent>();
        ManagedCallStack = managedCallStack?.ToArray() ?? Array.Empty<LiveParityManagedFrame>();
        RecentManagedEvents = recentManagedEvents?.ToArray() ?? Array.Empty<ParityTraceRecord>();
        ComparedComparableEvents = comparedComparableEvents;
        FocusRecipe = LiveParityFocusedTraceRecipe.Create(ReferenceRecord, ManagedRecord, MatchedContext);
    }

    public ParityTraceRecord? ReferenceRecord { get; }

    public ParityTraceRecord? ManagedRecord { get; }

    public IReadOnlyList<LiveParityMatchedEvent> MatchedContext { get; }

    public IReadOnlyList<LiveParityManagedFrame> ManagedCallStack { get; }

    public IReadOnlyList<ParityTraceRecord> RecentManagedEvents { get; }

    public int ComparedComparableEvents { get; }

    public LiveParityFocusedTraceRecipe? FocusRecipe { get; }

    public string ToDetailedReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Message);
        builder.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "Comparable events matched before abort: {0}",
                ComparedComparableEvents));

        if (MatchedContext.Count == 0)
        {
            builder.AppendLine("No comparable events matched before the abort.");
        }
        else
        {
            builder.AppendLine("Last matched comparable events:");
            foreach (LiveParityMatchedEvent match in MatchedContext)
            {
                builder.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "  [{0}] {1}",
                        match.ComparableIndex,
                        DescribeMatchedEvent(match)));
            }
        }

        if (ReferenceRecord is not null)
        {
            builder.AppendLine($"Reference mismatch event: {DescribeRecord(ReferenceRecord)}");
        }

        if (ManagedRecord is not null)
        {
            builder.AppendLine($"Managed mismatch event: {DescribeRecord(ManagedRecord)}");
        }

        if (ManagedCallStack.Count > 0)
        {
            LiveParityManagedFrame owningFrame = ManagedCallStack[^1];
            builder.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Managed owning scope hint: {0} (entered at seq={1})",
                    owningFrame.Scope,
                    owningFrame.Sequence));

            if (ManagedCallStack.Count > 1)
            {
                LiveParityManagedFrame parentFrame = ManagedCallStack[^2];
                builder.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Managed parent scope hint: {0} (entered at seq={1})",
                        parentFrame.Scope,
                        parentFrame.Sequence));
            }

            builder.AppendLine("Managed active call stack:");
            foreach (LiveParityManagedFrame frame in ManagedCallStack)
            {
                builder.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "  scope={0} enterSeq={1}",
                        frame.Scope,
                        frame.Sequence));
            }
        }

        if (RecentManagedEvents.Count > 0)
        {
            builder.AppendLine("Recent managed event tail:");
            foreach (ParityTraceRecord recentEvent in RecentManagedEvents)
            {
                builder.AppendLine($"  {DescribeRecord(recentEvent)}");
            }
        }

        if (MatchedContext.Count > 0)
        {
            ParityTraceRecord lastMatchedReference = MatchedContext[^1].ReferenceRecord;
            if (ReferenceRecord is not null &&
                ManagedRecord is not null &&
                string.Equals(ReferenceRecord.Kind, ManagedRecord.Kind, StringComparison.Ordinal) &&
                string.Equals(ReferenceRecord.Name ?? string.Empty, ManagedRecord.Name ?? string.Empty, StringComparison.Ordinal))
            {
                builder.AppendLine(
                    "Boundary hint: the comparator localized the divergence inside this trace event, so the next step is to compare the producer inputs feeding this block.");
            }
            else
            {
                builder.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Boundary hint: the last matched comparable event was {0}. If the mismatch first appears in a downstream consumer, add producer tracing between that event and the mismatch boundary above.",
                        DescribeRecord(lastMatchedReference)));
            }
        }

        if (FocusRecipe is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Focused rerun recipe:");
            foreach (string line in FocusRecipe.ToDisplayLines())
            {
                builder.AppendLine($"  {line}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeMatchedEvent(LiveParityMatchedEvent match)
    {
        var parts = BuildRecordSummaryParts(match.ReferenceRecord);
        parts.Add($"refSeq={match.ReferenceRecord.Sequence}");
        parts.Add($"manSeq={match.ManagedRecord.Sequence}");
        return string.Join(" ", parts);
    }

    private static string DescribeRecord(ParityTraceRecord record)
    {
        var parts = BuildRecordSummaryParts(record);
        parts.Add($"seq={record.Sequence}");
        return string.Join(" ", parts);
    }

    private static List<string> BuildRecordSummaryParts(ParityTraceRecord record)
    {
        var parts = new List<string>
        {
            $"kind={record.Kind}"
        };

        if (!string.IsNullOrWhiteSpace(record.Name))
        {
            parts.Add($"name={record.Name}");
        }

        if (!string.IsNullOrWhiteSpace(record.Scope))
        {
            parts.Add($"scope={record.Scope}");
        }

        AppendDataValue(parts, record, "iteration");
        AppendDataValue(parts, record, "side");
        AppendDataValue(parts, record, "station");
        AppendDataValue(parts, record, "iv");
        AppendDataValue(parts, record, "category");
        AppendDataValue(parts, record, "index");
        AppendDataValue(parts, record, "fieldIndex");
        AppendDataValue(parts, record, "panelIndex");
        AppendDataValue(parts, record, "jo");
        AppendDataValue(parts, record, "jp");
        AppendDataValue(parts, record, "jq");
        AppendDataValue(parts, record, "row");
        AppendDataValue(parts, record, "col");
        return parts;
    }

    private static void AppendDataValue(List<string> parts, ParityTraceRecord record, string key)
    {
        if (record.TryGetDataField(key, out JsonElement element))
        {
            parts.Add($"{key}={element}");
        }
    }
}

internal sealed record LiveParityMatchedEvent(
    int ComparableIndex,
    ParityTraceRecord ReferenceRecord,
    ParityTraceRecord ManagedRecord);

internal sealed record LiveParityManagedFrame(
    long Sequence,
    string Scope);

internal sealed class LiveParityFocusedTraceRecipe
{
    private static readonly string[] TriggerIdentityKeys =
    {
        "iteration",
        "side",
        "station",
        "iv",
        "category",
        "index",
        "fieldIndex",
        "panelIndex",
        "jo",
        "jp",
        "jq",
        "row",
        "col",
        "mode"
    };

    private LiveParityFocusedTraceRecipe(
        IReadOnlyList<string> captureKinds,
        string triggerKind,
        string? triggerScope,
        string? triggerName,
        IReadOnlyList<KeyValuePair<string, string>> triggerDataMatches,
        int ringBufferSize,
        int postTriggerLimit)
    {
        CaptureKinds = captureKinds;
        TriggerKind = triggerKind;
        TriggerScope = triggerScope;
        TriggerName = triggerName;
        TriggerDataMatches = triggerDataMatches;
        RingBufferSize = ringBufferSize;
        PostTriggerLimit = postTriggerLimit;
    }

    public IReadOnlyList<string> CaptureKinds { get; }

    public string TriggerKind { get; }

    public string? TriggerScope { get; }

    public string? TriggerName { get; }

    public IReadOnlyList<KeyValuePair<string, string>> TriggerDataMatches { get; }

    public int RingBufferSize { get; }

    public int PostTriggerLimit { get; }

    public static LiveParityFocusedTraceRecipe? Create(
        ParityTraceRecord? referenceRecord,
        ParityTraceRecord? managedRecord,
        IReadOnlyList<LiveParityMatchedEvent> matchedContext)
    {
        ParityTraceRecord? triggerRecord = managedRecord ?? referenceRecord;
        if (triggerRecord is null || string.IsNullOrWhiteSpace(triggerRecord.Kind))
        {
            return null;
        }

        var captureKinds = new List<string> { triggerRecord.Kind };
        for (int index = matchedContext.Count - 1; index >= 0; index--)
        {
            string candidateKind = matchedContext[index].ReferenceRecord.Kind;
            if (string.Equals(candidateKind, triggerRecord.Kind, StringComparison.Ordinal) ||
                captureKinds.Contains(candidateKind, StringComparer.Ordinal))
            {
                continue;
            }

            captureKinds.Insert(0, candidateKind);
            break;
        }

        var triggerDataMatches = new List<KeyValuePair<string, string>>();
        foreach (string key in TriggerIdentityKeys)
        {
            if (TryGetComparableDataValue(managedRecord, key, out string? managedValue) &&
                TryGetComparableDataValue(referenceRecord, key, out string? referenceValue))
            {
                if (managedValue is not null &&
                    referenceValue is not null &&
                    string.Equals(managedValue, referenceValue, StringComparison.Ordinal))
                {
                    triggerDataMatches.Add(new KeyValuePair<string, string>(key, managedValue));
                }

                continue;
            }

            if ((managedRecord is null || referenceRecord is null) &&
                TryGetComparableDataValue(triggerRecord, key, out string? fallbackValue))
            {
                if (fallbackValue is not null)
                {
                    triggerDataMatches.Add(new KeyValuePair<string, string>(key, fallbackValue));
                }
            }
        }

        return new LiveParityFocusedTraceRecipe(
            captureKinds,
            triggerRecord.Kind,
            string.IsNullOrWhiteSpace(triggerRecord.Scope) ? null : triggerRecord.Scope,
            string.IsNullOrWhiteSpace(triggerRecord.Name) ? null : triggerRecord.Name,
            triggerDataMatches,
            ringBufferSize: 8,
            postTriggerLimit: 1);
    }

    public IEnumerable<string> ToDisplayLines()
    {
        yield return "The managed focused rerun can reuse the full reference trace because the live comparator will skip non-selected reference events.";
        yield return $"export XFOIL_TRACE_KIND_ALLOW='{string.Join(",", CaptureKinds)}'";
        yield return $"export XFOIL_TRACE_TRIGGER_KIND='{TriggerKind}'";

        if (!string.IsNullOrWhiteSpace(TriggerScope))
        {
            yield return $"export XFOIL_TRACE_TRIGGER_SCOPE='{TriggerScope}'";
        }

        if (!string.IsNullOrWhiteSpace(TriggerName))
        {
            yield return $"export XFOIL_TRACE_TRIGGER_NAME_ALLOW='{TriggerName}'";
        }

        if (TriggerDataMatches.Count > 0)
        {
            string dataMatch = string.Join(
                ";",
                TriggerDataMatches.Select(pair => $"{pair.Key}={pair.Value}"));
            yield return $"export XFOIL_TRACE_TRIGGER_DATA_MATCH='{dataMatch}'";
        }

        yield return $"export XFOIL_TRACE_RING_BUFFER='{RingBufferSize.ToString(CultureInfo.InvariantCulture)}'";
        yield return $"export XFOIL_TRACE_POST_LIMIT='{PostTriggerLimit.ToString(CultureInfo.InvariantCulture)}'";
    }

    private static bool TryGetComparableDataValue(ParityTraceRecord? record, string key, out string? value)
    {
        value = null;
        if (record is null || !record.TryGetDataField(key, out JsonElement element))
        {
            return false;
        }

        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long int64Value)
                ? int64Value.ToString(CultureInfo.InvariantCulture)
                : element.GetDouble().ToString("R", CultureInfo.InvariantCulture),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };

        return !string.IsNullOrWhiteSpace(value);
    }
}

internal sealed class ParityTraceLiveComparator
{
    private const int DefaultMatchedContextWindow = 6;
    private const int DefaultManagedRecentEventWindow = 12;
    private static readonly string[] ComparableIdentityKeys =
    {
        "iteration",
        "side",
        "station",
        "iv",
        "category",
        "index",
        "fieldIndex",
        "panelIndex",
        "jo",
        "jp",
        "jq",
        "row",
        "col",
        "sourceIndex",
        "stage",
        "phase"
    };
    private readonly string _referenceTracePath;
    private readonly IReadOnlyList<ParityTraceRecord> _referenceRecords;
    private readonly ParityTraceFocusSelector? _captureSelector;
    private readonly ParityTraceFocusSelector? _triggerSelector;
    private readonly HashSet<string> _referenceComparableKinds;
    private readonly HashSet<string> _referenceComparableSignatures;
    private readonly Dictionary<string, List<int>> _referenceComparableSignatureIndices;
    private readonly Queue<LiveParityMatchedEvent> _matchedContext = new();
    private readonly List<LiveParityManagedFrame> _managedCallStack = new();
    private readonly Queue<ParityTraceRecord> _recentManagedEvents = new();
    private readonly int _matchedContextWindow;
    private readonly int _recentManagedEventWindow;
    private readonly bool _useImplicitManagedBootstrapAlignment;
    private int _referenceIndex;
    private int _comparedComparableEvents;
    private int _managedTriggerMatchCount;
    private bool _managedTriggerReached;

    public ParityTraceLiveComparator(
        string referenceTracePath,
        int matchedContextWindow = DefaultMatchedContextWindow,
        ParityTraceFocusSelector? captureSelector = null,
        ParityTraceFocusSelector? triggerSelector = null,
        int recentManagedEventWindow = DefaultManagedRecentEventWindow)
    {
        _referenceTracePath = referenceTracePath ?? throw new ArgumentNullException(nameof(referenceTracePath));
        _matchedContextWindow = Math.Max(1, matchedContextWindow);
        _recentManagedEventWindow = Math.Max(1, recentManagedEventWindow);
        _referenceRecords = ParityTraceLoader.ReadAll(referenceTracePath);
        _captureSelector = captureSelector;
        _triggerSelector = triggerSelector;
        _referenceComparableKinds = _referenceRecords
            .Where(record => IsComparable(record) && MatchesCapture(record))
            .Select(record => record.Kind)
            .ToHashSet();
        _referenceComparableSignatures = _referenceRecords
            .Where(record => IsComparable(record) && MatchesCapture(record))
            .Select(BuildComparableSignature)
            .ToHashSet(StringComparer.Ordinal);
        _referenceComparableSignatureIndices = BuildReferenceComparableSignatureIndices();
        _referenceIndex = DetermineInitialReferenceIndex(out _useImplicitManagedBootstrapAlignment);
        _managedTriggerReached = _triggerSelector is null && !_useImplicitManagedBootstrapAlignment;
    }

    public void ObserveSerializedRecord(string jsonLine)
    {
        ParityTraceRecord? managed = ParityTraceLoader.DeserializeLine(jsonLine);
        if (managed is null)
        {
            return;
        }

        ObserveManagedContext(managed);
        if (!HasReachedManagedTrigger(managed))
        {
            return;
        }

        if (!IsComparable(managed) || !MatchesCapture(managed) || !ExistsInReference(managed))
        {
            return;
        }

        ParityTraceRecord reference = NextComparableReference(managed);
        Compare(reference, managed);
        RememberMatched(reference, managed);
    }

    public IReadOnlyCollection<string> ReferenceComparableKinds => _referenceComparableKinds;

    public void AssertCompleted()
    {
        while (_referenceIndex < _referenceRecords.Count)
        {
            ParityTraceRecord candidate = _referenceRecords[_referenceIndex++];
            if (IsComparable(candidate) && MatchesCapture(candidate))
            {
                throw new LiveParityTraceMismatchException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Live parity mismatch after managed trace completion. Reference trace '{0}' still has comparable events beginning at kind={1}.",
                        _referenceTracePath,
                        candidate.Kind),
                    referenceRecord: candidate,
                    matchedContext: _matchedContext.ToArray(),
                    managedCallStack: _managedCallStack.ToArray(),
                    recentManagedEvents: _recentManagedEvents.ToArray(),
                    comparedComparableEvents: _comparedComparableEvents);
            }
        }
    }

    private ParityTraceRecord NextComparableReference(ParityTraceRecord managed)
    {
        string managedSignature = BuildComparableSignature(managed);
        while (_referenceIndex < _referenceRecords.Count)
        {
            ParityTraceRecord candidate = _referenceRecords[_referenceIndex++];
            if (IsComparable(candidate) && MatchesCapture(candidate))
            {
                if (!SignaturesMatch(candidate, managed))
                {
                    if (HasFutureComparableSignature(managedSignature, _referenceIndex))
                    {
                        continue;
                    }

                    return candidate;
                }

                ComparableIdentityMatch identityMatch = CompareComparableIdentity(candidate, managed);
                if (identityMatch == ComparableIdentityMatch.Mismatch &&
                    HasFutureComparableIdentityMatch(managedSignature, managed, _referenceIndex))
                {
                    continue;
                }

                return candidate;
            }
        }

        throw new LiveParityTraceMismatchException(
            $"Managed trace emitted an extra comparable event after reference trace '{_referenceTracePath}' was exhausted.",
            managedRecord: managed,
            matchedContext: _matchedContext.ToArray(),
            managedCallStack: _managedCallStack.ToArray(),
            recentManagedEvents: _recentManagedEvents.ToArray(),
            comparedComparableEvents: _comparedComparableEvents);
    }

    private static bool IsComparable(ParityTraceRecord record)
    {
        return record.Kind is not (
            "session_start" or
            "session_end" or
            "call_enter" or
            "call_exit" or
            "mode_toggle" or
            "legacy_line" or
            "legacy_fragment" or
            "naca4_config" or
            "panel_node") &&
            !string.Equals(record.Scope, "ViscousSolverEngine.TraceBufferGeometry", StringComparison.Ordinal);
    }

    private bool MatchesCapture(ParityTraceRecord record)
    {
        return _captureSelector?.Matches(record) ?? true;
    }

    private bool ExistsInReference(ParityTraceRecord record)
    {
        return _referenceComparableSignatures.Contains(BuildComparableSignature(record));
    }

    private static string BuildComparableSignature(ParityTraceRecord record)
    {
        string name = string.IsNullOrWhiteSpace(record.Name) ? "*" : record.Name;
        return $"{record.Kind}\u001F{name}";
    }

    private static bool SignaturesMatch(ParityTraceRecord reference, ParityTraceRecord managed)
    {
        return string.Equals(
            BuildComparableSignature(reference),
            BuildComparableSignature(managed),
            StringComparison.Ordinal);
    }

    private Dictionary<string, List<int>> BuildReferenceComparableSignatureIndices()
    {
        var indices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int index = 0; index < _referenceRecords.Count; index++)
        {
            ParityTraceRecord record = _referenceRecords[index];
            if (!IsComparable(record) || !MatchesCapture(record))
            {
                continue;
            }

            string signature = BuildComparableSignature(record);
            if (!indices.TryGetValue(signature, out List<int>? signatureIndices))
            {
                signatureIndices = new List<int>();
                indices[signature] = signatureIndices;
            }

            signatureIndices.Add(index);
        }

        return indices;
    }

    private bool HasFutureComparableSignature(string signature, int startIndex)
    {
        if (!_referenceComparableSignatureIndices.TryGetValue(signature, out List<int>? indices))
        {
            return false;
        }

        int position = indices.BinarySearch(startIndex);
        if (position < 0)
        {
            position = ~position;
        }

        return position < indices.Count;
    }

    private bool HasFutureComparableIdentityMatch(string signature, ParityTraceRecord managed, int startIndex)
    {
        if (!_referenceComparableSignatureIndices.TryGetValue(signature, out List<int>? indices))
        {
            return false;
        }

        int position = indices.BinarySearch(startIndex);
        if (position < 0)
        {
            position = ~position;
        }

        for (int index = position; index < indices.Count; index++)
        {
            ParityTraceRecord candidate = _referenceRecords[indices[index]];
            if (CompareComparableIdentity(candidate, managed) == ComparableIdentityMatch.Match)
            {
                return true;
            }
        }

        return false;
    }

    private int DetermineInitialReferenceIndex(out bool useImplicitManagedBootstrapAlignment)
    {
        useImplicitManagedBootstrapAlignment = false;
        int? triggerAnchor = TryFindTriggerReferenceIndex();
        if (triggerAnchor is not null)
        {
            return triggerAnchor.Value;
        }

        int bootstrapStartCount = 0;
        for (int index = 0; index < _referenceRecords.Count; index++)
        {
            if (!IsPanelingBootstrapStart(_referenceRecords[index]))
            {
                continue;
            }

            bootstrapStartCount++;
            if (bootstrapStartCount == 2)
            {
                useImplicitManagedBootstrapAlignment = true;
                return index;
            }
        }

        return 0;
    }

    private int? TryFindTriggerReferenceIndex()
    {
        if (_triggerSelector is null)
        {
            return null;
        }

        int matchedTriggerCount = 0;
        for (int index = 0; index < _referenceRecords.Count; index++)
        {
            ParityTraceRecord record = _referenceRecords[index];
            if (!IsComparable(record) || !MatchesCapture(record) || !_triggerSelector.Matches(record))
            {
                continue;
            }

            matchedTriggerCount++;
            if (_triggerSelector.Occurrence is null || matchedTriggerCount == _triggerSelector.Occurrence.Value)
            {
                return index;
            }
        }

        return null;
    }

    private bool HasReachedManagedTrigger(ParityTraceRecord managed)
    {
        if (_managedTriggerReached)
        {
            return true;
        }

        if (_useImplicitManagedBootstrapAlignment)
        {
            if (!IsPanelingBootstrapStart(managed))
            {
                return false;
            }

            _managedTriggerReached = true;
            return true;
        }

        if (_triggerSelector is null || !_triggerSelector.Matches(managed))
        {
            return false;
        }

        _managedTriggerMatchCount++;
        if (_triggerSelector.Occurrence is not null &&
            _managedTriggerMatchCount != _triggerSelector.Occurrence.Value)
        {
            return false;
        }

        _managedTriggerReached = true;
        return true;
    }

    private static bool IsPanelingBootstrapStart(ParityTraceRecord record)
    {
        if (!string.Equals(record.Kind, "pangen_snew_node", StringComparison.Ordinal))
        {
            return false;
        }

        return TryGetIntData(record, "iteration", out int iteration) && iteration == 0 &&
               TryGetIntData(record, "index", out int index) && index == 1 &&
               TryGetStringData(record, "stage", out string? stage) &&
               string.Equals(stage, "initial", StringComparison.Ordinal);
    }

    private static bool TryGetIntData(ParityTraceRecord record, string key, out int value)
    {
        value = 0;
        if (!record.TryGetDataField(key, out JsonElement element))
        {
            return false;
        }

        return element.TryGetInt32(out value);
    }

    private static bool TryGetStringData(ParityTraceRecord record, string key, out string? value)
    {
        value = null;
        if (!record.TryGetDataField(key, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private void ObserveManagedContext(ParityTraceRecord record)
    {
        if (ShouldTrackManagedEvent(record))
        {
            _recentManagedEvents.Enqueue(record);
            while (_recentManagedEvents.Count > _recentManagedEventWindow)
            {
                _ = _recentManagedEvents.Dequeue();
            }
        }

        switch (record.Kind)
        {
            case "call_enter":
                _managedCallStack.Add(new LiveParityManagedFrame(record.Sequence, record.Scope));
                break;

            case "call_exit":
                RemoveManagedFrame(record.Scope);
                break;
        }
    }

    private static bool ShouldTrackManagedEvent(ParityTraceRecord record)
    {
        return record.Kind is not ("session_start" or "session_end" or "legacy_line" or "legacy_fragment");
    }

    private void RemoveManagedFrame(string scope)
    {
        for (int index = _managedCallStack.Count - 1; index >= 0; index--)
        {
            if (string.Equals(_managedCallStack[index].Scope, scope, StringComparison.Ordinal))
            {
                _managedCallStack.RemoveRange(index, _managedCallStack.Count - index);
                return;
            }
        }
    }

    private void RememberMatched(ParityTraceRecord reference, ParityTraceRecord managed)
    {
        _comparedComparableEvents++;
        _matchedContext.Enqueue(new LiveParityMatchedEvent(_comparedComparableEvents, reference, managed));
        while (_matchedContext.Count > _matchedContextWindow)
        {
            _ = _matchedContext.Dequeue();
        }
    }

    private void Compare(ParityTraceRecord reference, ParityTraceRecord managed)
    {
        if (!string.Equals(reference.Kind, managed.Kind, StringComparison.Ordinal))
        {
            throw BuildMismatch(
                reference,
                managed,
                $"kind mismatch: reference={reference.Kind} managed={managed.Kind}");
        }

        string referenceName = reference.Name ?? string.Empty;
        string managedName = managed.Name ?? string.Empty;
        if (!string.Equals(referenceName, managedName, StringComparison.Ordinal))
        {
            throw BuildMismatch(
                reference,
                managed,
                $"name mismatch: reference='{referenceName}' managed='{managedName}'");
        }

        CompareJsonObject("data", reference.Data, managed.Data, reference, managed);
        CompareValues(reference, managed);
        CompareTags(reference, managed);
    }

    private static ComparableIdentityMatch CompareComparableIdentity(ParityTraceRecord reference, ParityTraceRecord managed)
    {
        bool foundSharedIdentity = false;
        foreach (string key in ComparableIdentityKeys)
        {
            if (!TryGetComparableIdentityValue(reference, key, out string? referenceValue) ||
                !TryGetComparableIdentityValue(managed, key, out string? managedValue))
            {
                continue;
            }

            foundSharedIdentity = true;
            if (!string.Equals(referenceValue, managedValue, StringComparison.Ordinal))
            {
                return ComparableIdentityMatch.Mismatch;
            }
        }

        return foundSharedIdentity
            ? ComparableIdentityMatch.Match
            : ComparableIdentityMatch.None;
    }

    private static bool TryGetComparableIdentityValue(ParityTraceRecord record, string key, out string? value)
    {
        value = null;
        if (!record.TryGetDataField(key, out JsonElement element))
        {
            return false;
        }

        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long int64Value)
                ? int64Value.ToString(CultureInfo.InvariantCulture)
                : element.GetDouble().ToString("R", CultureInfo.InvariantCulture),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private enum ComparableIdentityMatch
    {
        None,
        Match,
        Mismatch
    }

    private void CompareValues(ParityTraceRecord reference, ParityTraceRecord managed)
    {
        int referenceCount = reference.Values?.Length ?? 0;
        int managedCount = managed.Values?.Length ?? 0;
        if (referenceCount != managedCount)
        {
            throw BuildMismatch(
                reference,
                managed,
                $"values length mismatch: reference={referenceCount} managed={managedCount}");
        }

        if (reference.Values is null || managed.Values is null)
        {
            return;
        }

        for (int index = 0; index < reference.Values.Length; index++)
        {
            string selector = $"values[{index}]";
            string? referenceBits = TryResolveValueBits(reference, index);
            string? managedBits = TryResolveValueBits(managed, index);
            if (referenceBits is not null && managedBits is not null)
            {
                if (!string.Equals(referenceBits, managedBits, StringComparison.Ordinal))
                {
                    throw BuildMismatch(
                        reference,
                        managed,
                        $"{selector} bit mismatch: reference={reference.Values[index].ToString("G17", CultureInfo.InvariantCulture)} [{referenceBits}] managed={managed.Values[index].ToString("G17", CultureInfo.InvariantCulture)} [{managedBits}]");
                }

                continue;
            }

            if (!string.Equals(
                    reference.Values[index].ToString("R", CultureInfo.InvariantCulture),
                    managed.Values[index].ToString("R", CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
            {
                throw BuildMismatch(
                    reference,
                    managed,
                    $"{selector} mismatch: reference={reference.Values[index].ToString("G17", CultureInfo.InvariantCulture)} managed={managed.Values[index].ToString("G17", CultureInfo.InvariantCulture)}");
            }
        }
    }

    private void CompareTags(ParityTraceRecord reference, ParityTraceRecord managed)
    {
        int referenceCount = reference.Tags?.Count ?? 0;
        int managedCount = managed.Tags?.Count ?? 0;
        if (referenceCount != managedCount)
        {
            throw BuildMismatch(
                reference,
                managed,
                $"tag count mismatch: reference={referenceCount} managed={managedCount}");
        }

        if (reference.Tags is null || managed.Tags is null)
        {
            return;
        }

        foreach ((string key, JsonElement referenceValue) in reference.Tags.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!managed.Tags.TryGetValue(key, out JsonElement managedValue))
            {
                throw BuildMismatch(reference, managed, $"tag '{key}' missing on managed side");
            }

            CompareJsonElement(
                $"tags.{key}",
                referenceValue,
                managedValue,
                reference,
                managed,
                TryResolveTagBits(reference, key),
                TryResolveTagBits(managed, key));
        }
    }

    private void CompareJsonObject(
        string selector,
        JsonElement referenceElement,
        JsonElement managedElement,
        ParityTraceRecord reference,
        ParityTraceRecord managed)
    {
        if (referenceElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined &&
            managedElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (referenceElement.ValueKind != JsonValueKind.Object || managedElement.ValueKind != JsonValueKind.Object)
        {
            CompareJsonElement(selector, referenceElement, managedElement, reference, managed, null, null);
            return;
        }

        Dictionary<string, JsonElement> managedProperties = managedElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        foreach (JsonProperty referenceProperty in referenceElement.EnumerateObject())
        {
            if (!managedProperties.TryGetValue(referenceProperty.Name, out JsonElement managedValue))
            {
                throw BuildMismatch(reference, managed, $"{selector}.{referenceProperty.Name} missing on managed side");
            }

            CompareJsonProperty(
                $"{selector}.{referenceProperty.Name}",
                referenceProperty.Value,
                managedValue,
                reference,
                managed);
        }

    }

    private void CompareJsonProperty(
        string selector,
        JsonElement referenceElement,
        JsonElement managedElement,
        ParityTraceRecord reference,
        ParityTraceRecord managed)
    {
        string dataPath = selector["data.".Length..];
        CompareJsonElement(
            selector,
            referenceElement,
            managedElement,
            reference,
            managed,
            TryResolveDataBits(reference, dataPath),
            TryResolveDataBits(managed, dataPath));
    }

    private void CompareJsonElement(
        string selector,
        JsonElement referenceElement,
        JsonElement managedElement,
        ParityTraceRecord reference,
        ParityTraceRecord managed,
        IReadOnlyDictionary<string, string>? referenceBits,
        IReadOnlyDictionary<string, string>? managedBits)
    {
        if (referenceElement.ValueKind != managedElement.ValueKind)
        {
            if (TryCompareBooleanishKinds(
                    selector,
                    referenceElement,
                    managedElement,
                    reference,
                    managed))
            {
                return;
            }

            throw BuildMismatch(
                reference,
                managed,
                $"{selector} kind mismatch: reference={referenceElement.ValueKind} managed={managedElement.ValueKind}");
        }

        switch (referenceElement.ValueKind)
        {
            case JsonValueKind.Number:
                if (referenceBits is not null && managedBits is not null)
                {
                    if (TryResolveFloatingBitPattern(referenceBits, out string? referenceFloatBits) &&
                        TryResolveFloatingBitPattern(managedBits, out string? managedFloatBits))
                    {
                        if (!string.Equals(referenceFloatBits, managedFloatBits, StringComparison.Ordinal))
                        {
                            throw BuildMismatch(
                                reference,
                                managed,
                                $"{selector} bit mismatch: reference={referenceElement.GetRawText()} [{referenceFloatBits}] managed={managedElement.GetRawText()} [{managedFloatBits}]");
                        }

                        return;
                    }

                    if (TryResolveIntegerBitPattern(referenceBits, out string? referenceIntegerBits) &&
                        TryResolveIntegerBitPattern(managedBits, out string? managedIntegerBits))
                    {
                        if (!string.Equals(referenceIntegerBits, managedIntegerBits, StringComparison.Ordinal))
                        {
                            throw BuildMismatch(
                                reference,
                                managed,
                                $"{selector} bit mismatch: reference={referenceElement.GetRawText()} [{referenceIntegerBits}] managed={managedElement.GetRawText()} [{managedIntegerBits}]");
                        }

                        return;
                    }

                    if (AreJsonNumbersEquivalent(referenceElement, managedElement))
                    {
                        return;
                    }

                    throw BuildMismatch(
                        reference,
                        managed,
                        $"{selector} mismatch: reference={referenceElement.GetRawText()} [{ResolvePreferredBitPattern(referenceBits)}] managed={managedElement.GetRawText()} [{ResolvePreferredBitPattern(managedBits)}]");
                }

                if (TryCompareSingleEquivalentUsingAvailableBits(referenceElement, managedElement, referenceBits, managedBits))
                {
                    return;
                }

                if (!AreJsonNumbersEquivalent(referenceElement, managedElement))
                {
                    throw BuildMismatch(
                        reference,
                        managed,
                        $"{selector} mismatch: reference={referenceElement.GetRawText()} managed={managedElement.GetRawText()}");
                }

                return;

            case JsonValueKind.String:
                if (!string.Equals(referenceElement.GetString(), managedElement.GetString(), StringComparison.Ordinal))
                {
                    throw BuildMismatch(
                        reference,
                        managed,
                        $"{selector} mismatch: reference='{referenceElement.GetString()}' managed='{managedElement.GetString()}'");
                }

                return;

            case JsonValueKind.True:
            case JsonValueKind.False:
                if (referenceElement.GetBoolean() != managedElement.GetBoolean())
                {
                    throw BuildMismatch(
                        reference,
                        managed,
                        $"{selector} mismatch: reference={referenceElement.GetBoolean()} managed={managedElement.GetBoolean()}");
                }

                return;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return;

            case JsonValueKind.Array:
                JsonElement.ArrayEnumerator referenceArray = referenceElement.EnumerateArray();
                JsonElement.ArrayEnumerator managedArray = managedElement.EnumerateArray();
                JsonElement[] referenceItems = referenceArray.ToArray();
                JsonElement[] managedItems = managedArray.ToArray();
                if (referenceItems.Length != managedItems.Length)
                {
                    throw BuildMismatch(
                        reference,
                        managed,
                        $"{selector} length mismatch: reference={referenceItems.Length} managed={managedItems.Length}");
                }

                for (int index = 0; index < referenceItems.Length; index++)
                {
                    CompareJsonElement($"{selector}[{index}]", referenceItems[index], managedItems[index], reference, managed, null, null);
                }

                return;

            case JsonValueKind.Object:
                Dictionary<string, JsonElement> managedProperties = managedElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                foreach (JsonProperty referenceProperty in referenceElement.EnumerateObject())
                {
                    if (!managedProperties.TryGetValue(referenceProperty.Name, out JsonElement managedValue))
                    {
                        throw BuildMismatch(reference, managed, $"{selector}.{referenceProperty.Name} missing on managed side");
                    }

                    CompareJsonElement($"{selector}.{referenceProperty.Name}", referenceProperty.Value, managedValue, reference, managed, null, null);
                }

                return;

            default:
                if (!string.Equals(referenceElement.ToString(), managedElement.ToString(), StringComparison.Ordinal))
                {
                    throw BuildMismatch(
                        reference,
                        managed,
                        $"{selector} mismatch: reference={referenceElement} managed={managedElement}");
                }

                return;
        }
    }

    private bool TryCompareBooleanishKinds(
        string selector,
        JsonElement referenceElement,
        JsonElement managedElement,
        ParityTraceRecord reference,
        ParityTraceRecord managed)
    {
        if (!TryReadBooleanish(referenceElement, out bool? referenceValue) ||
            !TryReadBooleanish(managedElement, out bool? managedValue))
        {
            return false;
        }

        if (referenceValue == managedValue)
        {
            return true;
        }

        throw BuildMismatch(
            reference,
            managed,
            $"{selector} booleanish mismatch: reference={referenceElement.GetRawText()} managed={managedElement.GetRawText()}");
    }

    private static bool TryReadBooleanish(JsonElement element, out bool? value)
    {
        value = null;

        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;

            case JsonValueKind.False:
                value = false;
                return true;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out long int64Value))
                {
                    if (int64Value == 0)
                    {
                        value = false;
                        return true;
                    }

                    if (int64Value == 1)
                    {
                        value = true;
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private static string ResolvePreferredBitPattern(IReadOnlyDictionary<string, string> bits)
    {
        if (bits.TryGetValue("f32", out string? single))
        {
            return single;
        }

        if (bits.TryGetValue("f64", out string? @double))
        {
            return @double;
        }

        if (bits.TryGetValue("i32", out string? int32))
        {
            return int32;
        }

        if (bits.TryGetValue("i64", out string? int64))
        {
            return int64;
        }

        return bits.Values.FirstOrDefault() ?? string.Empty;
    }

    private static bool TryResolveFloatingBitPattern(
        IReadOnlyDictionary<string, string> bits,
        out string? bitPattern)
    {
        if (bits.TryGetValue("f32", out string? single))
        {
            bitPattern = single;
            return true;
        }

        if (bits.TryGetValue("f64", out string? @double))
        {
            bitPattern = @double;
            return true;
        }

        bitPattern = null;
        return false;
    }

    private static bool TryResolveIntegerBitPattern(
        IReadOnlyDictionary<string, string> bits,
        out string? bitPattern)
    {
        if (bits.TryGetValue("i32", out string? int32))
        {
            bitPattern = int32;
            return true;
        }

        if (bits.TryGetValue("i64", out string? int64))
        {
            bitPattern = int64;
            return true;
        }

        bitPattern = null;
        return false;
    }

    private static bool AreJsonNumbersEquivalent(JsonElement referenceElement, JsonElement managedElement)
    {
        if (referenceElement.TryGetInt64(out long referenceInt64) &&
            managedElement.TryGetInt64(out long managedInt64))
        {
            return referenceInt64 == managedInt64;
        }

        return referenceElement.GetDouble().Equals(managedElement.GetDouble());
    }

    private static bool TryCompareSingleEquivalentUsingAvailableBits(
        JsonElement referenceElement,
        JsonElement managedElement,
        IReadOnlyDictionary<string, string>? referenceBits,
        IReadOnlyDictionary<string, string>? managedBits)
    {
        return TryCompareSingleEquivalent(referenceElement, managedElement, referenceBits) ||
               TryCompareSingleEquivalent(referenceElement, managedElement, managedBits);
    }

    private static bool TryCompareSingleEquivalent(
        JsonElement referenceElement,
        JsonElement managedElement,
        IReadOnlyDictionary<string, string>? bits)
    {
        if (bits is null || !bits.TryGetValue("f32", out string? singleBits))
        {
            return false;
        }

        if (!TryParseHexBits(singleBits, out uint expectedBits))
        {
            return false;
        }

        uint referenceBits = unchecked((uint)BitConverter.SingleToInt32Bits((float)referenceElement.GetDouble()));
        uint managedBits = unchecked((uint)BitConverter.SingleToInt32Bits((float)managedElement.GetDouble()));
        return referenceBits == expectedBits && managedBits == expectedBits;
    }

    private static bool TryParseHexBits(string value, out uint bits)
    {
        ReadOnlySpan<char> text = value.AsSpan();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bits);
    }

    private static IReadOnlyDictionary<string, string>? TryResolveDataBits(ParityTraceRecord record, string path)
    {
        return record.TryGetDataBits(path, out IReadOnlyDictionary<string, string>? bits)
            ? bits
            : null;
    }

    private static IReadOnlyDictionary<string, string>? TryResolveTagBits(ParityTraceRecord record, string tag)
    {
        return record.TryGetTagBits(tag, out IReadOnlyDictionary<string, string>? bits)
            ? bits
            : null;
    }

    private static string? TryResolveValueBits(ParityTraceRecord record, int index)
    {
        if (record.TryGetValueBits(index, out IReadOnlyDictionary<string, string>? bits) && bits is not null)
        {
            return ResolvePreferredBitPattern(bits);
        }

        return null;
    }

    private LiveParityTraceMismatchException BuildMismatch(
        ParityTraceRecord reference,
        ParityTraceRecord managed,
        string detail)
    {
        return new LiveParityTraceMismatchException(
            string.Format(
                CultureInfo.InvariantCulture,
                "Live parity mismatch at kind={0} name={1} side={2} station={3} iteration={4}. {5}",
                managed.Kind,
                managed.Name ?? "*",
                GetDataValue(managed, "side") ?? GetDataValue(reference, "side") ?? "?",
                GetDataValue(managed, "station") ?? GetDataValue(reference, "station") ?? "?",
                GetDataValue(managed, "iteration") ?? GetDataValue(reference, "iteration") ?? "?",
                detail),
            reference,
            managed,
            _matchedContext.ToArray(),
            _managedCallStack.ToArray(),
            _recentManagedEvents.ToArray(),
            _comparedComparableEvents);
    }

    private static string? GetDataValue(ParityTraceRecord record, string key)
    {
        if (!record.TryGetDataField(key, out JsonElement element))
        {
            return null;
        }

        return element.ToString();
    }
}
