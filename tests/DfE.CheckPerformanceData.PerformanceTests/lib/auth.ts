import http from "k6/http";

export type AuthenticationRole =
    | "admin"
    | "editor";

/**
 * Authenticates the current k6 execution context
 * against a development impersonation endpoint.
 *
 * This must run at the start of every iteration.
 * k6 clears the VU cookie jar between iterations, so
 * a login made in an earlier iteration is not kept.
 * Caching the role across iterations causes later
 * iterations to run unauthenticated.
 */
export function authenticate(
    baseUrl: string,
    role: AuthenticationRole
): void {
    const response =
        http.get(
            `${baseUrl}/dev/impersonate/${role}`,
            {
                redirects: 0,

                tags: {
                    transaction:
                        `authenticate-${role}`,
                },
            }
        );

    if (
        response.status < 200 ||
        response.status >= 400
    ) {
        const location =
            response.headers.Location ??
            response.headers.location ??
            "";

        throw new Error(
            "Authentication failed: " +
            `role=${role}, ` +
            `status=${response.status}, ` +
            `location=${location}`
        );
    }
}