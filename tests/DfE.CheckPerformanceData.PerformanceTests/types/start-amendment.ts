import type {
    AmendmentType,
} from "./amendment";

export interface StartedAmendment {
    location: string;
    amendmentType: AmendmentType;
}