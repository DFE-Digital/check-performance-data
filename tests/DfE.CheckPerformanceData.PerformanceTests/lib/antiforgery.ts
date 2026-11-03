import type {
    Response,
} from "k6/http";

/**
 * Extracts the ASP.NET Core anti-forgery token
 * from an HTML response.
 *
 * Throws when the response does not contain the
 * expected __RequestVerificationToken input.
 */
export function getAntiforgeryToken(
    response: Response
): string {
    const token =
        response
            .html()
            .find(
                'input[name="__RequestVerificationToken"]'
            )
            .attr("value");

    if (!token) {
        throw new Error(
            "Anti-forgery token not found on " +
            `${response.url}.`
        );
    }

    return token;
}