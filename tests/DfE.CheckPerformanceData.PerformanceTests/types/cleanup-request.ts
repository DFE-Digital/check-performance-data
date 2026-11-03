import type {
    Response,
} from "k6/http";

export interface CleanupRequestOptions {
    baseUrl: string;
    windowId: string;
    requestReferenceNumber: string;
}

export interface CleanupRequestResult {
    redirectLocation: string;
    response: Response;
}