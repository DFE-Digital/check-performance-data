namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// One node of the predicate tree, flattened so the default model binder round-trips it.
/// Order among siblings = order in the list. See <see cref="PredicateForm"/>.
/// </summary>
public sealed class PredicateNodeForm
{
    public int Id { get; set; }            // stable form-local id, unique within the form
    public int? ParentId { get; set; }     // null for the root node
    public PredicateKind Kind { get; set; }

    public string? Field { get; set; }     // leaf field (also the country field for OfficialLanguageIs)
    public string? Operator { get; set; }  // UI operator token: eq|neq|in|lt|lte|gt|gte|known|lang
    public string? Op { get; set; }        // CompareOp name; set by LeafNormalizer.Normalize and PredicateForm.Flatten for FieldCompare
    public string? Value { get; set; }     // scalar literal (also the language for OfficialLanguageIs)
    public List<string> Values { get; set; } = new(); // for FieldIn
}
