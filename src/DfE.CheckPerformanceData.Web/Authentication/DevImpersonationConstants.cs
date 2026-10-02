namespace DfE.CheckPerformanceData.Web.Authentication;

// Names shared between the cookie scheme, the claims transformer, the impersonation
// controller, the header partial and the E2E test client. One central definition so a
// rename in any of those places trips the compiler everywhere else.
public static class DevImpersonationConstants
{
    public const string Scheme = "DevImpersonation";
    public const string CookieName = "cypd-dev-impersonation";
    public const string EditorValue = "editor";
    public const string UserValue = "user";
    public const string AdminValue = "admin";

    /// <summary>
    /// Same privilege as <see cref="EditorValue"/> but a GIAS establishment type of "11"
    /// (independent school). Exists so the two independent-related journey conditions — which
    /// gate removal reasons in opposite polarities — can be driven in a browser; every other
    /// value is a type "1" school. Dev-only, like the whole scheme.
    /// </summary>
    public const string IndependentUserValue = "independent";
}
