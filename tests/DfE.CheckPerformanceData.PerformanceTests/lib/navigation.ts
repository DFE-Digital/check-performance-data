import http, {
    Response,
} from "k6/http";

import type {
    RequestTags,
} from "../types/http";

import type {
    FormData,
} from "../types/form-data";

import type {
    EvidenceFile,
} from "../types/evidence";

export function getPage(
    baseUrl: string,
    path: string,
    tags: RequestTags = {}
): Response {

    return http.get(
        absoluteUrl(
            baseUrl,
            path
        ),
        {
            redirects: 0,
            tags,
        }
    );
}

export function postForm(
    baseUrl: string,
    path: string,
    formData: FormData,
    tags: RequestTags = {}
): Response {

    /*
     * k6 form bodies only accept single string values,
     * so forms with repeated fields are encoded here.
     * Forms without arrays keep the k6-encoded body.
     */
    const hasRepeatedFields =
        Object.values(formData)
            .some(Array.isArray);

    if (hasRepeatedFields) {
        return http.post(
            absoluteUrl(
                baseUrl,
                path
            ),
            encodeFormBody(formData),
            {
                redirects: 0,
                tags,
                headers: {
                    "Content-Type":
                        "application/x-www-form-urlencoded",
                },
            }
        );
    }

    return http.post(
        absoluteUrl(
            baseUrl,
            path
        ),
        formData as Record<string, string>,
        {
            redirects: 0,
            tags,
        }
    );
}

/**
 * Posts a multipart form that contains one file field.
 * Used for the evidence upload, which the browser sends
 * as multipart/form-data.
 */
export function postMultipartFile(
    baseUrl: string,
    path: string,
    fields: Record<string, string>,
    fileField: string,
    file: EvidenceFile,
    tags: RequestTags = {}
): Response {

    return http.post(
        absoluteUrl(
            baseUrl,
            path
        ),
        {
            ...fields,

            [fileField]:
                http.file(
                    file.data,
                    file.name,
                    file.contentType
                ),
        },
        {
            redirects: 0,
            tags,
        }
    );
}

/**
 * Encodes form data as application/x-www-form-urlencoded.
 * Array values are written as repeated name=value pairs.
 */
export function encodeFormBody(
    formData: FormData
): string {
    const pairs: string[] = [];

    for (const [name, value] of Object.entries(formData)) {
        const values =
            Array.isArray(value)
                ? value
                : [value];

        for (const item of values) {
            pairs.push(
                `${encodeURIComponent(name)}=` +
                `${encodeURIComponent(item)}`
            );
        }
    }

    return pairs.join("&");
}

export function absoluteUrl(
    baseUrl: string,
    path: string
): string {

    if (
        path.startsWith("http://") ||
        path.startsWith("https://")
    ) {
        return path;
    }

    return `${baseUrl}${path}`;
}