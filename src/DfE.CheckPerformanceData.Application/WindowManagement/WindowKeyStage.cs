using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The key stage a window type belongs to. The admin wizard does not ask for it:
/// <see cref="WindowService"/> sets <c>CheckingWindowDto.KeyStage</c> from this on every create and
/// update, so a window's key stage cannot disagree with its type, and a change of type moves it.
/// </summary>
/// <remarks>
/// There is <b>no default case</b>, the same rule as <see cref="LearnerNoun"/>: a new
/// <see cref="CheckingWindowType"/> must state its key stage rather than inherit one.
/// </remarks>
public static class WindowKeyStage
{
    public static KeyStages For(CheckingWindowType type) => type switch
    {
        CheckingWindowType.KS2 => KeyStages.KS2,
        CheckingWindowType.KS4June or CheckingWindowType.KS4Autumn => KeyStages.KS4,
        CheckingWindowType.Post16 => KeyStages.Post16,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type,
            "No key stage is defined for this checking window type.")
    };
}
