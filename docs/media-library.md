# Media library

The media library holds the images that content pages, rich text and content blocks show. An
editor uploads an image once and uses it in many places. When the editor replaces the file, every
current page shows the new file. Older page versions keep the file they were saved with.

Admin pages: **Admin > Content > Media** (`/admin/media`).

## Architecture

| Layer | Parts |
| --- | --- |
| Application (`Application/Media`) | `IMediaUploadService` (upload and replace), `IMediaFileCleaner` (type sniffing, limits, re-encoding, `SvgSanitiser`), `MediaProcessingGate`, `IMediaDeliveryService` (serving and renditions), `IMediaUrlBuilder` and `MediaUrlSigner` (signed URLs), `MediaReferenceExtractor` (finds images in content), `RichTextImageRewriter`, `IEmbeddedImageMover`, the reports in `Media/Reports` |
| Infrastructure | `MediaBlobStore` (the `cms-media` container), `SkiaRasterImageCleaner` and the rendition renderer (SkiaSharp), `BlobMediaUrlSigningKey` |
| Persistence | `MediaAssetRepository`, `MediaUsageWriter`, `EmbeddedImageStore`, the `media_assets`, `media_asset_versions` and `media_usages` tables |
| Web | `MediaLibraryController`, `MediaPickerController`, `MediaReportsController`, `MediaFileController` (`/media/...`), `media-library-upload.js`, `media-library-detail.js`, `media-picker.js`, `image-widget-editor.js`, `rich-text-media.js` |

The flow:

1. An editor uploads a file. The cleaner checks it and makes the bytes to store.
2. The upload service stores the bytes in blob storage, then saves the rows.
3. Content stores only the image id: the Image widget's `mediaId` prop, or
   `<img data-media-id="{id}" src="/media/{id}/...">` in rich text.
4. When a page renders, the server finds the version to show and writes signed URLs.
5. `/media/...` serves the stored file, or a resized copy that it makes once and keeps.

## Data model

- **`media_assets`** (`MediaAsset`): one row for each image. `Id` (chosen by the caller, kept by
  content staging), `FileName`, `Slug` (for the URL), `DefaultAltText`, `Caption`, `Credit`,
  `Tags`, `CurrentVersion`, and who created and last changed it, with the times.
- **`media_asset_versions`** (`MediaAssetVersion`): one row for each stored file of an image. The
  key is (`AssetId`, `Version`). It holds `BlobKey`, `ContentType`, `Width`, `Height`,
  `SizeBytes`, `Sha256` (of the stored bytes), `IsAnimated`, and who uploaded it, with the time.
  A version row never changes after insert.
- **`media_usages`** (`MediaUsage`): one use of an image. A row belongs to a page version
  (`PageNodeId` and `PageNodeVersionId`) or to a content block (`ContentBlockId`), never both
  (`ck_media_usages_owner`). `AssetVersion` is the image version that the use is pinned to.
  `Location` is `widget` or `richtext`.

See [domain-model.md](domain-model.md) for when the rows are written.

## Version rules

- Stored content never holds an image version.
- Live, scheduled, draft and preview pages show the image's current version. So "Replace the
  file" changes every current page at once.
- A page version opened from the version history shows the version that its `media_usages` row
  pins. It shows the current version when there is no pin.
- A content block always shows the current version.
- Replacing a file adds version N+1 and moves the pins of current page versions and blocks to it,
  in the same save. Older page versions keep their pins.
- An image that any page version or block uses cannot be deleted.

## Serving and signed renditions

The public URL is `/media/{id}/{version}/{slug}.{ext}`. It is anonymous, and it answers GET and
HEAD. The id and version choose the file. The slug and extension are for people only.

- **Original:** a URL with no settings serves the stored file.
- **Rendition:** `w` (width), `crop=x,y,w,h`, `ar` (aspect ratio, for example `16:9`) and `focal=x,y`
  (only with `ar`) ask for a resized or cropped copy. Fractions are 0 to 1, rounded to 3 decimal
  places.
- **Widths:** only 320, 640, 960, 1280 and 1920 (`MediaRenditionQuery.AllowedWidths`).
- **Signature:** a rendition URL must carry `sig`. It is an HMAC-SHA256 of the id, the version and
  the canonical settings, cut to 128 bits, in URL-safe base64. Only the server makes these URLs,
  when a page renders.
- A missing or wrong signature, a width outside the set or a bad setting gets **400**. An unknown
  id or version gets a bare **404**. The service checks the query before it reads the database.
- Copies are WebP. An SVG, an animation or an image of unknown size is always served as stored.
- A copy is made once and kept under `renditions/` (see [blob-storage.md](blob-storage.md)).
- Every file served has `Cache-Control: public, max-age=31536000, immutable`, an ETag and
  `X-Content-Type-Options: nosniff`.

### The signing key

`BlobMediaUrlSigningKey` keeps the key in blob `media-url-signing-key` in the
`data-protection-keys` container. It is 32 random bytes, encrypted with ASP.NET Data Protection.
The first instance that needs the key makes it, and every replica reads the same blob. No secret
is set in any environment. If the key can no longer be decrypted, a new key replaces it. That is
safe, because pages make media URLs when they render.

## Uploads

### Limits

| Limit | Value |
| --- | --- |
| File size | 20 MB (`MediaUploadLimits.MaxFileBytes`) |
| Pixels | 50 megapixels |
| Side | 10,000 pixels |
| Animation frames | 500 |
| Pixels in all frames of an animation | 250 megapixels |
| SVG size | 5,000,000 characters |
| SVG nesting | 256 levels |

The size and pixel limits are read from the file header before any pixels are decoded. So a
small file that claims a huge image (a decompression bomb) is refused at once. Kestrel's request
size limit is off for the whole app, so each upload action sets its own limit.

### Cleaning

- The type comes from the file's first bytes, never from its name. PNG, JPEG, GIF, WebP and SVG
  are accepted.
- A still image gets its EXIF orientation applied, then it is encoded again (PNG to PNG, JPEG to
  JPEG at quality 85, WebP to WebP). This removes EXIF and GPS data.
- An animated GIF or WebP is decoded to check it, then stored as uploaded, with its metadata
  chunks removed.
- **Duplicates:** the SHA-256 of the cleaned bytes. An upload that matches an image's current
  version is not stored again. The editor is offered the image that is already there.

### SVG sanitising

`SvgSanitiser` builds a new document from an allow-list:

- A DOCTYPE with an internal subset (entities or declarations) is refused. So is any entity
  reference, a file that does not parse, or a root that is not `svg`.
- A DOCTYPE that only names a DTD is removed. The DTD is never fetched.
- Elements outside the list are removed with their content. This removes `script` and
  `foreignObject`. Comments and processing instructions are removed.
- Attributes outside the list are removed. This removes every `on*` handler.
- A link may only point inside the file (`#id`). On `image`, it can also be a PNG, JPEG, GIF or
  WebP `data:` URL.
- A value with `javascript:`, `vbscript:`, another `data:` URL, `expression()`, `@import`,
  `image-set()` or a `url()` that is not `#id` is removed.

### Content security policy for SVG

An SVG is served with its own policy, which replaces the site policy:
`default-src 'none'; style-src 'unsafe-inline'; sandbox`. So an SVG opened on its own runs no
script and loads nothing.

### Processing at the same time

Decoding a 50 megapixel image needs about 400 MB of memory. `MediaProcessingGate` lets only
`Media:MaxConcurrentProcessing` images decode at once, across uploads, the embedded-image move,
content import and new renditions. The default is 2. Other requests wait at the gate.

## Permissions

- The **`media`** admin section manages the library: the library page, image pages, metadata,
  replace, delete, the reports and the embedded-image move.
- The **picker** (`/admin/media/picker`, its preview and `/admin/media/picker/upload`) is gated on
  **`content-pages`**. An editor who builds pages can browse the library and upload into it from
  the Image widget and the rich text editor, without the `media` section.
- The content-block editors show the picker only to a user who has `content-pages`.
- A new database gives `media` to the admin role and to the editor role. **On a database that
  existed before the media library, tick Media for the editor role in Role settings.** The admin
  role gets every section on each start.

## Moving embedded images

`/admin/media/move-embedded` (section `media`) finds `data:` images in the current versions of
pages (live, scheduled and draft) and in content blocks.

1. The first page is a dry run. It lists each page and block, and how many images it will move.
2. **Move images** uploads each image to the library and changes the content to use it.
3. An image that the library already holds is used again.

- The change is made in place. It makes no new page version. A draft's minor version goes up. A
  live or scheduled version keeps its number and its dates.
- A content block gets a new block version, as an editor's save does.
- Each page or block changes completely or not at all. If the library refuses an image, the
  item is left as it was and the result lists it.
- The run can be run again. It carries on where it stopped.
- Only `<img>` tags are moved. A markdown image is not found.

## Reports

- **Alt text report** (`/admin/media/reports/alt-text`): library images with missing or weak
  default alt text, and each use in current content with missing or weak alt text.
  - Weak alt text is: none; a file name; a generic word alone ("image", "photo", "logo" and
    others); fewer than 5 characters; or the same as the caption.
  - A decorative use is not listed. In rich text, decorative means `alt=""` with
    `role="presentation"` (or `role="none"`). The rich text image dialog writes both when the
    editor ticks "Image is decorative". An `alt=""` with no role is listed as missing.
- **Size report** (`/admin/media/reports/size`): totals of images, stored files and versions, the
  space that resized copies use, and the 50 largest images.

## Content staging

A content-staging bundle carries the images that its pages and blocks use. The schema is still
`cpd-content-v2`, version 2. A bundle with no images imports as before.

- `mediaAssets[]`: `id`, `fileName`, `defaultAltText`, `caption`, `credit`, `tags[]`,
  `currentVersion`, and `versions[]` of `version`, `sha256`, `contentType`, `width`, `height`,
  `sizeBytes` and `isAnimated`.
- `mediaFiles[]`: `sha256` and `data` (base64). There is one entry for each different file.
- Each page version has `media[]` of `mediaId` and `version`: the version the page version was
  saved with.

Export carries each image that an exported page version or Content block uses, with its current
version and every version that an exported page version pins.

Import runs before the pages and blocks, and keeps each image's id. For each version:

1. The bytes must match their `sha256`. If not, the import gives a warning and skips the version.
2. A target version with that hash is used again.
3. If not, the bytes go through the upload cleaning. A target version with the hash of the
   cleaned bytes is used again.
4. If not, the cleaned bytes are stored as the next version.

- The bundle's current version is matched only with the target's current version. So the image
  always ends on the bundle's current file.
- An image that already exists keeps its own alt text, caption, credit and tags.
- Type, size and the animated flag come from the cleaning, never from the bundle.
- Every problem is a warning, not an error. A page that uses an image that is in neither the
  bundle nor the library gets a warning.
- The result says "Images: N added, N given a new version, N already here."
- **Limit:** images are base64 text inside `bundle.json`. They count against the 50 MB bundle
  limit and the 64 MB upload limit. For a large set of images, export fewer pages at a time.

## Development and test pages

The pages under `/development-testing` carry the media library samples:

| Id | File | Use |
| --- | --- | --- |
| `00000000-cd94-4a01-8f01-0000000e0201` | `blocks.png`, 1280 × 640 | Image test page, rich text page |
| `00000000-cd94-4a01-8f01-0000000e0202` | `blocks.svg` | Image test page, rich text page |
| `00000000-cd94-4a01-8f01-0000000e0203` | `landscape.jpg` | Rich text page, the `development-testing-image` content block |
| `00000000-cd94-4a01-8f01-0000000e0204` | `moving-square.gif` (animated, no default alt text) | Rich text page |

The images are in `Data/Import/development-testing.json`, with the pages, as `mediaAssets` and
`mediaFiles`. The import brings them in before the pages, so the pages record their uses. The
import runs again on each start, finds the same images and adds nothing.

Test pages:

- `/development-testing/media-library-test-page`: links to each part of the library, with steps
  to try it. It also has a hostile SVG to upload, and an SVG with a DTD that the library refuses.
- `/development-testing/widgets/image-test-page`: the Image widget at each width, crop and focal
  point.
- `/development-testing/media-rich-text-test-page`: rich text with library images, a figure, a
  weak alt text and a decorative image.
- `/development-testing/embedded-image-test-page`: a `data:` image for the embedded-image move.
  The next start, or "Seed sample CMS pages", puts it back after a move.
- Content block `development-testing-image`: a block with a library image.

Dev endpoints (`DevE2eSupportController`, editor role, `Dev:ToolsEnabled` and not Production):

- `POST /dev/media/sample?kind=png|svg[&alt=]`: puts a fixed sample image in the library through
  the upload pipeline and returns its id, version, URL and srcset.
- `POST /dev/media/usage?mediaId=&pageId=`: records that a page's latest version uses the image.
- `POST /dev/media/usage/clear?mediaId=`: removes every use of the image, so a test can delete it.

The dev sample files are different from the fixture samples, so a test that changes a dev sample
never changes a fixture image.
