#nullable enable
using System.Text.RegularExpressions;

namespace VB6Instrument;

/// <summary>How a guarded member's handler lets the error escape.</summary>
public enum GuardMode
{
    /// <summary>Decorate Err.Description and fall to End — VB6 propagates the
    /// error to the caller, which contributes a frame to the error chain.</summary>
    Reraise,

    /// <summary>Report (optional statement), Err.Clear, and fall to End — a
    /// clean normal exit with nothing pending to propagate.</summary>
    Swallow,
}

/// <summary>Strategy for the re-raise handler body. The Step-0 probe
/// (HandlerSemanticsProbe) established that an active VB6 handler falling
/// to End WITHOUT Resume swallows the error instead of propagating, so
/// Raise is the default: the handler decorates with the captured Erl and
/// re-raises explicitly, which reliably propagates to the caller.</summary>
public enum ErrorPropagation
{
    FallThrough,

    /// <summary>Default — probe-verified propagation.</summary>
    Raise,
}

/// <summary>
/// Pass C — error guards. Inserts <c>On Error GoTo GuardError_&lt;Member&gt;</c>
/// before the first code-bearing body line and a handler immediately before
/// the member's End line. Runs last (after traces) so the inserted On Error
/// line precedes the trace statement. Guard lines are unnumbered (numbering
/// already ran) and therefore do not shift Erl for original statements.
/// </summary>
public sealed partial class ErrorGuardPass
{
    /// <summary>Initial event suffix list (spec §5.4; extend as needed — a
    /// missed event name degrades to re-raise, which is the status quo).</summary>
    internal static readonly string[] EventSuffixes =
    [
        "Click", "DblClick", "Change", "Validate", "LostFocus", "GotFocus",
        "KeyDown", "KeyPress", "KeyUp", "MouseDown", "MouseMove", "MouseUp",
        "Scroll", "SelChange", "RowColChange", "Close", "Error", "Load",
        "Unload", "QueryUnload", "Activate", "Deactivate", "Timer", "Paint",
        "DragDrop", "DragOver", "OLEStartDrag", "OLEDragOver", "OLEDragDrop",
        "OLEGiveFeedback", "OLESetData", "OLECompleteDrag", "LinkClose",
        "LinkError", "LinkNotify", "LinkOpen", "EnterFocus", "ExitFocus",
        "ItemCheck", "ItemClick", "NodeClick", "ColumnClick",
        "AfterLabelEdit", "BeforeLabelEdit", "Connect", "Disconnect",
        "DataArrival", "SendComplete", "SendProgress",
    ];

    /// <summary>UserControl_* lifecycle members: no receiver exists that
    /// could handle the error, so they swallow.</summary>
    internal static readonly string[] UserControlLifecycle =
    [
        "Initialize", "InitProperties", "ReadProperties", "WriteProperties",
        "Terminate", "Show", "Hide", "Resize", "Paint", "AmbientChanged",
        "AsyncReadComplete", "AsyncReadProgress", "EnterFocus", "ExitFocus",
        "GotFocus", "LostFocus", "KeyDown", "KeyPress", "KeyUp",
        "MouseDown", "MouseMove", "MouseUp", "Click", "DblClick",
        "DragDrop", "DragOver", "OLEStartDrag", "OLEDragOver", "OLEDragDrop",
        "OLEGiveFeedback", "OLECompleteDrag",
    ];

    private const string GuardVariableName = "instrumentedGuardErl";

    private readonly string? _statementTemplate;
    private readonly ErrorPropagation _propagation;

    public ErrorGuardPass(string? statementTemplate, ErrorPropagation propagation)
    {
        _statementTemplate = statementTemplate;
        _propagation = propagation;
    }

    [GeneratedRegex(@"\bon\s+error\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OnErrorPattern();

    [GeneratedRegex(@"^[A-Za-z_]\w*$", RegexOptions.Compiled)]
    private static partial Regex BareIdentifierPattern();

    public PassResult Run(Vb6FileModel model)
    {
        Vb6FileParser.Refresh(model);
        var contents = model.Contents;
        var result = new PassResult("error-guard") { MembersSeen = model.Members.Count };
        var insertions = new List<LineInsertion>();
        var indent = new string(' ', PassHelper.NumberedBodyIndent);

        var guarded = 0;
        foreach (var range in model.Members) {
            if (CallTracePass.ContainsMarker(contents, range, "NoGuard")) {
                result.MembersSkipped++;
                continue;
            }

            // Members with pre-existing hand-written handlers manage their
            // own errors and must be left byte-identical. Detection uses
            // masked code only: string literals and comments don't count,
            // colon-compounds do.
            if (HasExistingOnError(model, range)) {
                result.MembersSkipped++;
                continue;
            }

            var insertIndex = PassHelper.FindFirstCodeLine(contents, range.FirstInsertableIndex, range.EndIndex);
            if (insertIndex < 0) {
                result.MembersSkipped++;
                continue;
            }

            var label = BuildUniqueLabel(model, range);
            var mode = Classify(range);
            guarded++;

            insertions.Add(new LineInsertion(insertIndex, indent + $"On Error GoTo {label}"));
            insertions.Add(new LineInsertion(range.EndIndex, indent + BuildExitStatement(range)));
            insertions.Add(new LineInsertion(range.EndIndex, $"{label}:"));

            if (mode == GuardMode.Reraise) {
                foreach (var line in BuildReraiseHandler(model, range)) {
                    insertions.Add(new LineInsertion(range.EndIndex, indent + line));
                }
            }
            else {
                if (_statementTemplate is not null) {
                    insertions.Add(new LineInsertion(range.EndIndex, indent + PassHelper.RenderTemplate(_statementTemplate, model.FileName, range.Name)));
                }
                insertions.Add(new LineInsertion(range.EndIndex, indent + "Err.Clear"));
            }
        }

        PassHelper.ApplyInsertions(model, insertions);
        result.MembersTouched = guarded;
        result.LinesChanged = insertions.Count;
        return result;
    }

    private static bool HasExistingOnError(Vb6FileModel model, Vb6MemberRange range)
    {
        foreach (var statement in model.Statements) {
            if (range.IsInRange(statement.StartLine - 1) && OnErrorPattern().IsMatch(statement.MaskedText)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The generated label must not collide with an existing label in the
    /// body; suffix-uniquify when it does. The statement tokenizer strips
    /// label colons, so a label surfaces as a colon-less single-identifier
    /// statement — whether bare ("Abortar:") or heading a colon compound
    /// ("Abortar: x = 1"). A parameterless implicit call would also match,
    /// which is harmless over-collection.
    /// </summary>
    private static string BuildUniqueLabel(Vb6FileModel model, Vb6MemberRange range)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in model.Statements) {
            if (!range.IsInRange(statement.StartLine - 1)) {
                continue;
            }
            if (BareIdentifierPattern().IsMatch(statement.Text)) {
                existing.Add(statement.Text);
            }
        }

        var baseName = "GuardError_" + PassHelper.SanitizeLabelPart(range.Name);
        var candidate = baseName;
        var suffix = 2;
        while (existing.Contains(candidate)) {
            candidate = $"{baseName}_{suffix++}";
        }
        return candidate;
    }

    /// <summary>Mode classification per spec §5.4.</summary>
    public static GuardMode Classify(Vb6MemberRange range)
    {
        var name = range.Name;
        if (name.Equals("Class_Terminate", StringComparison.OrdinalIgnoreCase)) {
            return GuardMode.Swallow;
        }
        if (name.Equals("Class_Initialize", StringComparison.OrdinalIgnoreCase)) {
            return GuardMode.Reraise;
        }
        if (name.StartsWith("UserControl_", StringComparison.OrdinalIgnoreCase)) {
            return GuardMode.Swallow;
        }

        var modifiers = range.Mods.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (modifiers.Any(m => m is "Public" or "Friend" or "Global")) {
            return GuardMode.Reraise;
        }

        var eventSuffix = SuffixAfterLastUnderscore(name);
        if (eventSuffix is not null && EventSuffixes.Contains(eventSuffix, StringComparer.OrdinalIgnoreCase)) {
            return GuardMode.Swallow;
        }

        return GuardMode.Reraise;
    }

    private static string? SuffixAfterLastUnderscore(string name)
    {
        var index = name.LastIndexOf('_');
        return index > 0 ? name[(index + 1)..] : null;
    }

    /// <summary>
    /// The exit statement matching the member kind — <c>Exit Sub</c>,
    /// <c>Exit Function</c>, or <c>Exit Property</c>. VB6 rejects a
    /// mismatched form (e.g. <c>Exit Sub</c> inside a Function) as a
    /// compile error, so every Property kind (Get/Let/Set) shares
    /// <c>Exit Property</c>.
    /// </summary>
    private static string BuildExitStatement(Vb6MemberRange range) => range.EndKind switch
    {
        "Sub" => "Exit Sub",
        "Function" => "Exit Function",
        "Property" => "Exit Property",
        _ => throw new InstrumentationException(null, $"Unknown member kind '{range.Kind}' for member '{range.Name}'."),
    };

    private IEnumerable<string> BuildReraiseHandler(Vb6FileModel model, Vb6MemberRange range)
    {
        if (_propagation == ErrorPropagation.FallThrough) {
            // The handler ends without Resume, so VB6 propagates the error
            // to the caller while the decorated description accumulates a
            // stack-trace-like chain (outermost frame first).
            yield return $"Err.Description = \"{model.FileName}.{range.Name} (\" & Erl & \") -> \" & Err.Description";
            yield break;
        }

        // Explicit re-raise resets Err.Erl, so the captured Erl is baked
        // into the decoration instead.
        yield return $"Dim {GuardVariableName} As Long: {GuardVariableName} = Erl";
        yield return $"Err.Raise Err.Number, Err.Source, \"{model.FileName}.{range.Name} (\" & {GuardVariableName} & \") -> \" & Err.Description";
    }
}
