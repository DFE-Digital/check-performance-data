namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>The check that a <see cref="DateRule"/> applies to a date answer.</summary>
public enum DateRuleCheck
{
    /// <summary>Fails when the date is later than UK today. Today passes.</summary>
    NotInFuture,

    /// <summary>Fails when the date is later than the target. Equal dates pass.</summary>
    OnOrBefore,

    /// <summary>Fails when the date is earlier than the target. Equal dates pass.</summary>
    OnOrAfter
}
