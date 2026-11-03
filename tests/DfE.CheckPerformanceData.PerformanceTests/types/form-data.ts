/*
 * A single form field value. Repeated fields (for
 * example, checkbox groups) are expressed as arrays
 * and encoded as repeated name=value pairs.
 */
export type FormValue =
    string | string[];

export type FormData =
    Record<string, FormValue>;