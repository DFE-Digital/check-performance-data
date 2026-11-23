# Media library

The media library holds the images that content pages, rich text and content blocks show. An
editor uploads an image once and uses it in many places. When the editor replaces the file, every
current page shows the new file. Older page versions keep the file they were saved with.

Admin pages: **Admin > Content > Media** (`/admin/media`).

## Architecture

| Layer | Parts |
| --- | --- |
| Application (`Application/Media`) | `IMediaUploadService` (upload and replace), `IMediaFileCleaner` (type sniffing, limits, re-encoding, `SvgSanitiser`), `MediaProcessingGate`, `IMediaDeliveryService` (serving and renditions), `IMediaUrlBuilder` and `MediaUrlSigner` (signed URLs), `MediaReferenceExtractor` (finds images in content), `RichTextImageRewriter`, `IEmbeddedImageMover`, the reports in `Media/Reports`, `IMediaVariantService` and `MediaVariantGrouper` (crops and sizes) |
| Infrastructure | `MediaBlobStore` (the `cms-media` container), `SkiaRasterImageCleaner` and the rendition renderer (SkiaSharp), `BlobMediaUrlSigningKey` |
| Persistence | `MediaAssetRepository`, `MediaFolderRepository`, `MediaUsageWriter`, `EmbeddedImageStore`, the `media_assets`, `media_asset_versions`, `media_usages` and `media_folders` tables |
| Web | `MediaLibraryController` (with "Delete unused images"), `MediaPickerController`, `MediaReportsController`, `MediaFileController` (`/media/...`), `media-library-upload.js`, `media-library-detail.js`, `media-picker.js`, `image-widget-editor.js`, `rich-text-media.js` |

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
  `Tags`, `CurrentVersion`, `FolderId` (null at the top of the library), and who created and
  last changed it, with the times.
- **`media_folders`** (`MediaFolder`): one row for each folder. `Id`, `Name`, `NameKey`
  (`lower(name)`, made by the database), `ParentId` (null at the top), and who created and last
  changed it, with the times.
- **`media_asset_versions`** (`MediaAssetVersion`): one row for each stored file of an image. The
  key is (`AssetId`, `Version`). It holds `BlobKey`, `ContentType`, `Width`, `Height`,
  `SizeBytes`, `Sha256` (of the stored bytes), `IsAnimated`, and who uploaded it, with the time.
  A version row never changes after insert.
- **`media_usages`** (`MediaUsage`): one use of an image. A row belongs to a page version
  (`PageNodeId` and `PageNodeVersionId`) or to a content block (`ContentBlockId`), never both
  (`ck_media_usages_owner`). `AssetVersion` is the image version that the use is pinned to.
  `Location` is `widget` or `richtext`.

Every reference is a foreign key that the database checks:

| Column | References | On delete |
| --- | --- | --- |
| `media_asset_versions.asset_id` | `media_assets.id` | cascade |
| `media_assets (id, current_version)` | `media_asset_versions (asset_id, version)` | refused. Checked at the end of the transaction, because an image and its first version are added in one save. The migration adds it in SQL |
| `media_usages (asset_id, asset_version)` | `media_asset_versions (asset_id, version)` | refused while a usage pins the version |
| `media_usages (page_node_version_id, page_node_id)` | `PageNodeVersions (Id, PageNodeId)` | cascade. A usage cannot name another page than its page version's page |
| `media_usages.page_node_id` | `PageNodes.Id` | cascade |
| `media_usages.content_block_id` | `ContentBlocks.Id` | cascade |
| `PageNodeVersions.SupersededById` | `PageNodeVersions.Id` | set null |
| `media_assets.folder_id` | `media_folders.id` | refused while an image is in the folder |
| `media_folders.parent_id` | `media_folders.id` | refused while a folder is in the folder |

Page content refers to an image by its id only, in JSON or HTML, which no key can check. The
`media_usages` rows are the checked record of those uses.

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
- "Current" page versions are the live version, each scheduled version and the working draft.
  A version that another version replaced (`SupersededById` is set) is never current.
- An image that any page version or block uses cannot be deleted.

## Image size in rich text

The image dialog in the rich text editor stores a `width` and a `height` on the `<img>`. When the
page renders, `RichTextImageRewriter` writes the size like this:

- A width and a height that the editor set both stay. This is true also when their shape is not
  the shape of the file. The editor can unlock "Constrain proportions" and stretch an image on
  purpose.
- When only one side is set, the other side follows the shape of the version shown.
- When no side is set, the image gets the size of the version shown.
- The image also gets a `style="aspect-ratio: W / H"` with the same two numbers. The page CSS
  sets `max-width: 100%` and `height: auto`, so an image shrinks in a narrow column. Without the
  aspect ratio, the shape of the file would then win over the editor's box. The rewriter makes
  this style from the two numbers. It removes a stored style, and the sanitiser removes style
  attributes before this step.

So a replacement file of another shape shows in the box that the editor set. To show the new
shape, the editor sets the size again in the image dialog.

## Serving and signed renditions

The public URL is `/media/{id}/{version}/{slug}.{ext}`. It is anonymous, and it answers GET and
HEAD. The id and version choose the file. The slug and extension are for people only.

- **Original:** a URL with no settings serves the stored file.
- **Rendition:** `w` (width), `crop=x,y,w,h`, `ar` (aspect ratio, for example `16:9`) and `focal=x,y`
  (only with `ar`) ask for a resized or cropped copy. Fractions are 0 to 1, rounded to 3 decimal
  places.
- The focal point matters only when `ar` cuts the image or a crop of another shape. So the Image
  widget editor disables "Set focal point" while a preset crop shape and a crop are both set. A
  hint says why. Free shape or "Reset crop" turns it on again.
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

## The library page

`/admin/media` uses the full-width admin layout (`ViewData["AdminWide"]`), so the grid fills the
screen. The "View" select above the images chooses how the page lists them:

| View | `view=` | Shows |
| --- | --- | --- |
| Grid | `grid` (or none) | A thumbnail, the file name, the size and a "No alt text" tag |
| Details | `details` | A table: a small thumbnail, the file name, and the facts one to a line |
| Large | `large` | One image to a row, a 960 pixel copy, with the same facts |

The facts are the type, the dimensions, the file size, the alt text, the uses and who uploaded
the current file, with the time (UTC). "Uses" is the number of pages and content blocks that use
the image, the same as "Where the image is used" on the image page
(`IMediaAssetRepository.CountUsesAsync`, one query for each page of the list).

The view is in the query string only. Page links, the search and filters form and "Clear search
and filters" keep it. A value that is not one of the three shows the grid. The service keeps no
view for each user.

An image that current content shows in more than one way has the link "Used in N ways" under it
in the grid, and a "Crops and sizes" row in the Details and Large views. The link opens the
"Crops and sizes" section of the image page (`#crops-and-sizes`). The counts for one page of the
list take two queries (see below).

## Crops and sizes

A crop is a setting of each use of an image. It is not a separate file. The Image widget stores
its crop, aspect ratio and focal point in its props. Rich text stores a width and a height on the
`<img>`. When a page renders, the server makes a signed rendition URL from the original file with
those settings (see "Serving and signed renditions"). So the library holds one file for each
image version, and cropping never changes it.

The image page shows how current content uses the image in a "Crops and sizes" section, under
the facts about the file. Current content is the live, scheduled and draft versions of pages that
are not deleted, and content blocks. Older page versions are not listed.

Each way is a GOV.UK summary card with:

- a title and the settings in words, for example "Cropped to 16:9, focal point centre-left" or
  "Shown at 320 × 320 in rich text";
- a preview, made from the current file through the same signed rendition URLs as a page. The
  preview of a rich-text size is the whole image in a box of that shape, because a rendition never
  stretches an image. Its alt text is the file name and the title;
- the pages and content blocks that show the image that way. Each link opens the editor. A page
  has a tag for each state that shows it this way: Live, Scheduled or Draft. "Image widget, 2
  times" means one version shows it twice.

### How uses are grouped

`MediaVariantGrouper` groups the uses with the rules the renderers use, so two uses that look the
same on a page are one card:

| Use | Key |
| --- | --- |
| Image widget | The crop, aspect ratio and focal point, rounded as a rendition URL rounds them (`MediaRenditionQuery.Normalise`). A focal point with no aspect ratio is dropped. A crop of the whole image with no aspect ratio is the original |
| Image widget showing an SVG or an animation | Always the original: these files are never cut |
| Rich text | The box that `RichTextImageRewriter.Box` gives the img. One side only gives the other side from the shape of the file. No size, or a box of the file's own size, is the original |
| A use that `media_usages` records but no `<img>` shows (a markdown image) | The original |

The widget's width and alignment are not part of the key: they place the image, and do not
change which part of it shows. There is no rotation setting.

The original comes first, then the crops, then the rich-text sizes. Pages come in title order,
then blocks in key order. A page whose live and draft versions show the image the same way is one
row with both tags. A draft that changes the crop puts the page under both cards.

"Used in N ways" counts the cards, the original included.

### Where the settings come from

`media_usages` records which image each page version and block uses, but not the settings of
each use. So `IMediaVariantService` reads them from the content each time:

- `IMediaReportStore.ListCurrentPageVersionsUsingAsync` reads the current page versions that
  record a use of any of the images, with their content and the images that each one records.
- `IMediaReportStore.ListContentBlocksUsingAsync` does the same for content blocks.
- `MediaImageUseReader.ReadShapes` reads each use's settings with the same tree walk as the alt
  text report.

That is two queries for the image page, and two queries for each page of the library list.

## Folders

The library is a tree of folders. An image is in one folder, or at the top of the library. A
folder does not change an image's id, URL or versions, so moving images never changes a page.

### Rules

- A name has 1 to 100 characters, with no slash and no control character. Spaces at the ends are
  removed.
- Two folders in the same place cannot have the same name, ignoring case. This is true at the top
  too.
- A folder cannot move into itself or into a folder inside it. `IMediaFolderService` refuses the
  move first. The trigger `trg_media_folders_refuse_cycle` refuses it again (constraint name
  `ck_media_folders_no_cycle`). The trigger takes a lock, so two moves at the same time that
  would make a loop cannot both pass.
- Only an empty folder can be deleted: no images and no folders in it. The page says what a full
  folder holds. Both foreign keys refuse the delete too.

### The admin navigation

The "Media library" entry in the admin navigation shows the folder tree under it, in name order
(`MediaFolderNavEntry`, grafted by `AdminNavNodeViewModel.GraftMediaFolders`). A folder opens
`/admin/media?folder={id}`. The layout reads the folders in one query. A user without the `media`
section sees neither the entry nor the folders.

### The library page in a folder

- `folder={id}` shows one folder. Breadcrumbs show its path. An unknown id gets 404.
- "Folders" lists the folders in it. "Create a folder" makes one inside the folder shown
  (`POST /admin/media/folders`).
- "Rename, move or delete this folder" opens `/admin/media/folders/{id}`. It has three forms:
  `…/rename`, `…/move` (`parent` is a folder id, or `top`) and `…/delete`.
- **Include subfolders.** Ticked, the list holds the folder and every folder inside it, at any
  depth. Unticked, it holds the folder only. At the top of the library, ticked is the whole
  library and unticked is the images in no folder.
- **The default is ticked.** So `/admin/media` lists every image, as before folders, and a
  search finds an image wherever it is. "Off" is `subfolders=false` in the address. The check box
  sends `true` before a hidden `false`, and the first value wins.
- The folder and `subfolders=false` stay in the address. Page links, the view select, the search
  and filters form and "Delete unused images" keep them. "Clear search and filters" keeps the
  folder and the view.
- Each image in the grid, details and large views has a "Select" check box. "Move selected
  images to" puts the ticked images in a folder, or at the top
  (`POST /admin/media/move`). The editor goes back to the same list.
- In the grid, an image that is not in the folder shown says where it is. The details and large
  views show the folder of each image.
- An upload goes into the folder shown (`/admin/media/upload?folder={id}`). An upload that
  matches an image already in the library offers that image, in whatever folder it is.

The subtree is one recursive SQL query (`MediaFolderQueries.SubtreeIds`), in the same SQL as the
list.

### The picker

The picker has a folder tree above the images, with "Media library" at the top, and the check box
"Find images in this folder and its subfolders" (ticked by default). The list endpoint takes the
same `folder` and `subfolders` values. The default (the top, with subfolders) adds nothing to the
address, so the first list is the whole library, as before.

- The folders are buttons in nested lists. The chosen folder is pressed (`aria-pressed`) and is
  the tree's one Tab stop. The up and down arrow keys, Home and End move between folders. Enter
  or Space chooses one.
- The dialog's Upload button puts the file in the chosen folder.
- A pasted or dropped image in rich text (`MediaPicker.upload`) goes to the top of the library.
- The folder stays chosen when the dialog opens again on the same page.

### Other pages

- The alt text report and the size report cover every folder, and show each image's folder.
- "Delete unused images" works across all folders from the top of the library. From a folder, it
  lists the unused images in that folder (with its subfolders when they are included), and shows
  a folder column.

## Permissions

- The **`media`** admin section manages the library: the library page, image pages, metadata,
  replace, delete, the reports and the embedded-image move.
- The **picker** (`/admin/media/picker`, its preview and `/admin/media/picker/upload`) is also
  gated on **`media`**. A user without it gets 404 from these endpoints.
- The picker dialog (`_MediaPicker.cshtml`) checks the grant itself. The page, wiki and
  content-block editors show no picker to a user without `media`.
- **The editors work without the picker.** The Image widget keeps its "Image ID" field, so an
  editor can type the id of an image that someone gives them. The link to the library is not
  shown. The rich text editor takes no pasted or dropped images.
- A new database gives `media` to the admin role (`cypmd_admin`) and to the editor role
  (`cypmd_content_access_user`).
- A database made before the media library gets `media` for both roles from the
  `GrantMediaSection` migration. EF runs a migration only once. So if an admin later removes Media
  from the editor role in Role settings, it stays removed.
- The admin role always has every section. Each start tops it up.

## Deleting unused images

Nothing deletes an image by itself: not a content import, and not a replace. An image that no
page version or content block uses stays in the library until an editor deletes it.

1. On the library page, tick **Not used on any page**, and search if you want fewer images.
2. Select **Delete unused images**. The confirmation page
   (`GET /admin/media/delete-unused?search=`) lists the unused images that match the search, by
   file name. One run takes at most 500 (`MediaDeletionService.MaxUnusedPerRun`).
3. Select **Delete N images** (`POST /admin/media/delete-unused`, antiforgery token).

- The post deletes only the images that the confirmation page listed.
- Each image goes through the same delete as the image page. It checks the image's uses again
  inside the delete. So an image that content started to use after the confirmation page stays,
  and the result says so.
- The pages need the `media` section.
- Each run writes one `MediaUnusedDelete` audit row (see [audit-log.md](audit-log.md)).

## Moving embedded images

`/admin/media/move-embedded` (section `media`) finds `data:` images in the current versions of
pages (live, scheduled and draft) and in content blocks.

1. The first page is a dry run. It lists each page and block, and how many images it will move.
2. **Move images** uploads each image to the library and changes the content to use it.
3. An image that the library already holds is used again.

- Each current version that changes gets a new page version with the next number. The run
  does what an editor's publish does, with the library images in place of the embedded ones:
  - A live version gets a new published version with the same "publish from" and "publish to".
  - A scheduled version gets a new scheduled version with the same dates.
  - A draft gets a new draft.
- Each new version takes the next free number for the page. The editor's draft keeps its number,
  so no two versions show the same number.
- The run sets the old version's `SupersededById` to the new version's id. The old version shows
  as "Replaced" in the version history. It is never live, scheduled or the working draft again,
  even when an editor re-dates, unschedules or unpublishes the new version. Its dates cannot be
  edited. **Publish** on its row takes it back on purpose.
- The old versions stay in the version history with their content. They keep their embedded
  images, and a preview of them still shows those images. Only the live flag and the
  replacement marker of an old version change.
- The URL, the live and draft versions and the schedule stay as they were. The new versions show
  the person who ran the move in "Modified by". A page version has no field for a note.
- `media_usages` rows are written for the new versions only.
- A page is skipped, and listed in the result, when an editor saves or publishes it during the
  run. The next run tries it again.
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
  `currentVersion`, `folderPath[]` of `id` and `name` (the folders that hold the image, from the
  top down; empty at the top), and `versions[]` of `version`, `sha256`, `contentType`, `width`,
  `height`, `sizeBytes` and `isAnimated`.
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
- **The bundle wins.** An image that already exists takes the bundle's alt text, caption, credit
  and tags. A blank value in the bundle clears the value here. The details are written only when
  they differ, so an import that changes nothing writes nothing.
- Import never deletes an image, even one that no content uses after the import. To remove
  unused images, use "Delete unused images" on the library page (see below).
- Type, size and the animated flag come from the cleaning, never from the bundle.
- **Folders.** Import finds each folder of an image's `folderPath` by its name in the folder
  before it, ignoring case. So the path wins over the id. A folder that is not there is made, with
  the bundle's id when that id is free. A new image goes into the last folder of the path. An
  existing image moves there, and counts as updated. A bad name or a path deeper than 20 folders
  gives a warning or an error. A bundle made before folders has no `folderPath`: a new image goes
  to the top, and an existing image stays where it is.
- The result adds "Image folders: N added." when the import made folders.
- Every problem is a warning, not an error. A page that uses an image that is in neither the
  bundle nor the library gets a warning.
- The result says "Images: N added, N updated, N already here." An image is updated when it gets
  a new version, new details, or both.
- **Limit:** images are base64 text inside `bundle.json`. They count against the 50 MB bundle
  limit and the 64 MB upload limit. For a large set of images, export fewer pages at a time.

## Development and test pages

The pages under `/development-testing` carry the media library samples:

| Id | File | Use |
| --- | --- | --- |
| `00000000-cd94-4a01-8f01-0000000e0201` | `blocks.png`, 1280 × 640 | Image test page (whole, 16:9 with a focal point, and the left half), rich text page (whole, and a 320 × 320 box). Its image page lists these 4 ways |
| `00000000-cd94-4a01-8f01-0000000e0202` | `blocks.svg` | Image test page, rich text page |
| `00000000-cd94-4a01-8f01-0000000e0203` | `landscape.jpg` | Rich text page, the `development-testing-image` content block |
| `00000000-cd94-4a01-8f01-0000000e0204` | `moving-square.gif` (animated, no default alt text) | Rich text page |

The PNG and the SVG are in the folder Samples / Graphics, and the photo is in Samples / Photos.
The animation is at the top of the library. The folders have fixed ids: Samples
`00000000-cd94-4a01-8f01-0000000e0301`, Graphics `…0302` and Photos `…0303`. Each start puts the
samples back in these folders.

The images are in `Data/Import/development-testing.json`, with the pages, as `mediaAssets` and
`mediaFiles`. The import brings them in before the pages, so the pages record their uses. The
import runs again on each start, finds the same images and adds nothing.

Test pages:

- `/development-testing/media-library-test-page`: links to each part of the library, with steps
  to try it, also for folders and for crops and sizes. It also has a hostile SVG to upload, and an SVG with a DTD that the library refuses.
- `/development-testing/widgets/image-test-page`: the Image widget at each width, crop and focal
  point.
- `/development-testing/media-rich-text-test-page`: rich text with library images, a figure, a
  weak alt text, a decorative image and a wide image that the editor stretched into a square box.
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
