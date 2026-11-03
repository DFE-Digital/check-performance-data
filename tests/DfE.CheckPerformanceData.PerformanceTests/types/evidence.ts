/**
 * A file uploaded as evidence on a journey page.
 * Loaded once in the k6 init context with open(path, "b").
 */
export interface EvidenceFile {
    name: string;
    contentType: string;
    data: ArrayBuffer;
}
