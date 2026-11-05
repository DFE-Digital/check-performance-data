import http from "k6/http";
import { parseHTML } from "k6/html";

import type {
    WindowBlobs,
    WindowProfile,
} from "../types/window.ts";

interface DataBlob {
    path: string;
    lastModified: string;
}

interface Listing {
    folders: string[];
    blobs: DataBlob[];
}

/*
 * Lists one folder of a window's admin storage through the
 * storage browser. The browser shows folders as links with
 * ?prefix= and blobs with a download link and a Last modified
 * cell, in the order: name, size, type, last modified, actions.
 */
function listFolder(
    baseUrl: string,
    windowId: string,
    prefix: string
): Listing {

    const response =
        http.get(
            `${baseUrl}/admin/storage/app/${windowId}` +
            `?prefix=${encodeURIComponent(prefix)}`
        );

    if (response.status !== 200) {
        throw new Error(
            `Failed to list ${prefix}. ` +
            `Status: ${response.status}`
        );
    }

    const listing: Listing = {
        folders: [],
        blobs: [],
    };

    const rows =
        parseHTML(response.body as string)
            .find("tr");

    for (let index = 0; index < rows.size(); index++) {
        const row = rows.eq(index);

        const folderHref =
            row.find("a[href*='?prefix=']")
                .first()
                .attr("href");

        if (folderHref) {
            listing.folders.push(
                decodeURIComponent(
                    folderHref.split("?prefix=")[1]
                )
            );
            continue;
        }

        const downloadHref =
            row.find("a[href*='/download?blob=']")
                .first()
                .attr("href");

        if (!downloadHref) {
            continue;
        }

        const lastModified =
            row.find("td")
                .eq(3)
                .text()
                .trim();

        if (!/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/.test(lastModified)) {
            throw new Error(
                `Blob in ${prefix} has no usable Last modified time`
            );
        }

        listing.blobs.push({
            path: decodeURIComponent(
                downloadHref.split("?blob=")[1]
            ),
            lastModified,
        });
    }

    return listing;
}

/*
 * Returns every blob under exercises/{exercise}/releases/
 * {release}/data/ for a window. Each release is a folder
 * of its own, so older releases stay in storage after a
 * reseed.
 */
function releaseDataBlobs(
    baseUrl: string,
    windowId: string
): DataBlob[] {

    const blobs: DataBlob[] = [];

    for (const exercise of listFolder(baseUrl, windowId, "exercises/").folders) {
        for (const release of listFolder(baseUrl, windowId, `${exercise}releases/`).folders) {
            blobs.push(
                ...listFolder(baseUrl, windowId, `${release}data/`).blobs
            );
        }
    }

    return blobs;
}

/*
 * Returns the blob path with this file name and the latest
 * Last modified time. Fails when two blobs share that time,
 * because the newest release cannot be picked.
 */
function newestBlob(
    blobs: DataBlob[],
    fileName: string
): string | undefined {

    const matches =
        blobs
            .filter((blob) => blob.path.endsWith(`/${fileName}`))
            .sort((a, b) =>
                a.lastModified < b.lastModified ? 1
                    : a.lastModified > b.lastModified ? -1
                        : 0
            );

    const [first, second] = matches;

    if (first && second && first.lastModified === second.lastModified) {
        throw new Error(
            `Two releases hold ${fileName} with the same Last modified time. ` +
            `Remove the older release from storage.`
        );
    }

    return first?.path;
}

/*
 * Finds each file of a window in its newest release. Fails
 * before any journey runs when a required file is missing.
 */
export function resolveWindowBlobs(
    baseUrl: string,
    profile: WindowProfile
): WindowBlobs {

    const dataBlobs =
        releaseDataBlobs(baseUrl, profile.windowId);

    const pupilBlob =
        newestBlob(dataBlobs, profile.pupilFile);

    if (!pupilBlob) {
        throw new Error(
            `No ${profile.pupilFile} found in any release of the ${profile.key} window`
        );
    }

    const found: WindowBlobs = {
        pupilBlob,
    };

    if (profile.resultsFile) {
        const resultsBlob =
            newestBlob(dataBlobs, profile.resultsFile);

        if (!resultsBlob) {
            throw new Error(
                `No ${profile.resultsFile} found in any release of the ${profile.key} window`
            );
        }

        found.resultsBlob = resultsBlob;
    }

    console.log(`${profile.key} pupil blob: ${found.pupilBlob}`);

    if (found.resultsBlob) {
        console.log(`${profile.key} results blob: ${found.resultsBlob}`);
    }

    return found;
}
