# Media library images are stored once and served through signed URLs

Date: 2026-11-21

Status: Accepted

## Context

Editors put images in content pages, rich text and content blocks. Before the media library, rich
text kept each image as a `data:` URI inside the page text. The same image was stored again on
each page, a page could not be resized for a phone, and a new file meant editing every page.

An image service on a public site must also not become a tool for others. A URL that resizes to
any width lets anyone make the server decode and encode images without limit. An uploaded SVG can
hold script.

## Decision

- Each image is a `media_assets` row with versions in `media_asset_versions`. A version row never
  changes. The file is in blob storage, in the `cms-media` container.
- Content stores only the image id. Pages find the version when they render: the current version
  for live and draft pages, and the pinned version (`media_usages`) for an older page version.
- Every upload is cleaned before it is stored. Still images are encoded again, which removes
  metadata. SVG is rebuilt from an allow-list and served with its own content security policy.
- `/media/{id}/{version}/...` serves the original to anyone. A resized or cropped copy needs a
  signature that only the server makes, and only fixed widths are allowed. Each copy is made once
  and kept.
- The signing key is a blob in the Data Protection container, encrypted with Data Protection, so
  no environment needs a new secret.

## Consequences

- Replacing a file changes every current page at once, and the page history still shows the old
  file.
- The server decodes only images that it linked to, at sizes it chose. Decoding also waits at a
  gate (`Media:MaxConcurrentProcessing`).
- A URL cannot be built by hand for a new size. Content and templates must get image URLs from
  `IMediaUrlBuilder`.
- An image that a page or block uses cannot be deleted.
- Images moved between environments travel inside the content-staging bundle, so they count
  against its size limit.

See [media-library.md](../media-library.md).
