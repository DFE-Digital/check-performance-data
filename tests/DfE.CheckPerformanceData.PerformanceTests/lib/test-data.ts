import http from "k6/http";

import {
    WINDOW_PROFILES,
} from "./windows.ts";

import type {
    Pupil,
} from "../types/pupil.ts";

import type {
    WindowBlobs,
    WindowKey,
} from "../types/window.ts";

/*
 * KS4 pupil record.
 */
interface Ks4PupilRecord {
    Id: string;
    CYPMD_ID: string;
    FORENAME: string;
    SURNAME: string;
    DOB: string;
    P_INCL: string | number;
    UPN: string;
    INCLUDED: boolean;
    INCLUDABLE: boolean;
}

/*
 * 16 to 19 student record.
 */
interface Post16PupilRecord {
    Id: string;
    CYPMD_ID: string;
    FORENAMES: string;
    SURNAME: string;
    DOB: string;
    INCLUDED: boolean;
    INCLUDABLE: boolean;
    ULN: string;
}

function mapKs4Record(
    record: Ks4PupilRecord
): Pupil {
    return {
        id:
            record.Id,

        cypmdId:
            record.CYPMD_ID,

        firstName:
            record.FORENAME,

        surname:
            record.SURNAME,

        dateOfBirth:
            record.DOB,

        inclusionCode:
            String(record.P_INCL),

        includable:
            record.INCLUDABLE,

        searchLabel:
            `${record.SURNAME}, ${record.FORENAME}, ` +
            `(CYPMD ID: ${record.CYPMD_ID}, UPN: ${record.UPN}, ` +
            `DOB: ${record.DOB}, ` +
            `Status: ${record.INCLUDED ? "INCLUDED" : "NOT INCLUDED"})`,
    };
}

function mapPost16Record(
    record: Post16PupilRecord
): Pupil {
    return {
        id:
            record.Id,

        cypmdId:
            record.CYPMD_ID,

        firstName:
            record.FORENAMES,

        surname:
            record.SURNAME,

        dateOfBirth:
            formatIsoDate(record.DOB),

        inclusionCode:
            record.INCLUDED
                ? "INCLUDED"
                : "NOT INCLUDED",

        includable:
            record.INCLUDABLE,

        searchLabel:
            `${record.FORENAMES}, ${record.SURNAME}, ` +
            `(CYPMD ID:${record.CYPMD_ID}, ULN:${record.ULN}, ` +
            `DOB:${formatIsoDate(record.DOB)}, ` +
            `${record.INCLUDED ? "INCLUDED" : "NOT INCLUDED"})`,
    };
}

/*
 * Converts "2007-01-01 00:00:00.0000000" to
 * "01/01/2007", the format the live suggestions use.
 */
function formatIsoDate(
    isoDate: string
): string {
    const [year, month, day] =
        isoDate.slice(0, 10).split("-");

    return `${day}/${month}/${year}`;
}

/*
 * 16 to 19 result row in the results file.
 */
interface ResultRecord {
    CYPMD_ID: string;
    QAN: string;
    SESSION: string;
    SOURCE: string;
    GRADE: string;
}

/*
 * Downloads a blob from a window's admin storage.
 */
function downloadBlob(
    baseUrl: string,
    windowId: string,
    blob: string,
    key: WindowKey
): string {

    const response =
        http.get(
            `${baseUrl}` +
            `/admin/storage/app/${windowId}` +
            `/download?blob=${encodeURIComponent(blob)}`
        );

    if (response.status !== 200) {
        throw new Error(
            `Failed to download ${blob} for ${key}. ` +
            `Status: ${response.status}`
        );
    }

    return response.body as string;
}

interface FirstResult {
    key: string;
    qan: string;
    grade: string;
}

/*
 * Maps each CYPMD_ID to its first result. The key is the
 * composite that the result search posts.
 */
function firstResultsByCypmdId(
    body: string
): Map<string, FirstResult> {

    const results =
        new Map<string, FirstResult>();

    for (const record of JSON.parse(body) as ResultRecord[]) {
        const cypmdId =
            String(record.CYPMD_ID);

        if (!results.has(cypmdId)) {
            results.set(cypmdId, {
                key: `${record.QAN}|${record.SESSION}|${record.SOURCE}`,
                qan: String(record.QAN),
                grade: String(record.GRADE)
            });
        }
    }

    return results;
}

/*
 * Downloads and maps the pupil records for a window.
 * Pupils with a result in the window's results blob
 * get that result's key.
 */
export function loadPupils(
    baseUrl: string,
    key: WindowKey,
    blobs: WindowBlobs
): Pupil[] {

    const profile =
        WINDOW_PROFILES[key];

    const body =
        downloadBlob(
            baseUrl,
            profile.windowId,
            blobs.pupilBlob,
            key
        );

    const pupils: Pupil[] =
        key === "post16"
            ? (JSON.parse(body) as Post16PupilRecord[]).map(mapPost16Record)
            : (JSON.parse(body) as Ks4PupilRecord[]).map(mapKs4Record);

    if (!blobs.resultsBlob) {
        return pupils;
    }

    const firstResults =
        firstResultsByCypmdId(
            downloadBlob(
                baseUrl,
                profile.windowId,
                blobs.resultsBlob,
                key
            )
        );

    return pupils.map((pupil) => {
        const result =
            firstResults.get(String(pupil.cypmdId));

        return result
            ? {
                ...pupil,
                resultKey: result.key,
                resultQan: result.qan,
                resultGrade: result.grade
            }
            : pupil;
    });
}