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
}
