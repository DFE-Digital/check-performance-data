---
title: If something looks wrong
subtitle: Common problems and how to put them right
order: 2
keywords: troubleshooting problem not showing missing 404 page not found cannot see error fix help
---

Most problems in the CMS have one of a few causes. Find what you are seeing below.

## My change is not showing on the site

| Check | What to do |
|---|---|
| Did you publish? | Your changes are kept as a draft. Open the page in the editor: a grey *Draft* tag means there are changes that are not live. Select **Publish draft**. |
| Did you save the widget? | Each widget has its own **Save** button. A change typed into a widget is lost if you leave without saving it. |
| Is a later version scheduled or live? | Open the **Versions** tab and check which version is *Live*. |
| Is your browser showing an old copy? | Reload the page. |

## Visitors get "Page not found"

| Check | What to do |
|---|---|
| Is the page published? | Editors see a preview of an unpublished page, so it can look fine to you. Open the **Versions** tab: if no version is *Live*, publish one. |
| Has a Publish to date passed? | On the **Versions** tab, look for a version with a **Publish to** date in the past. It may still be marked *Live*. Select **Publish** on the version you want. |
| Has the address changed? | Renaming a page, or moving it under a different parent, changes its address. Old links are not redirected. |
| Was it deleted? | Look in **Deleted pages** and restore it. |

## A page does not come up in search

| Check | What to do |
|---|---|
| Is it published? | Unpublished pages are never in search. |
| Is **Appear in search** ticked? | Check the **Properties** tab. |
| Is it a folder? | Folders are never in search. |
| Do the words people type appear on the page? | Add them to **Search keywords** on the **Properties** tab. |
| Is the search box limited to certain pages? | A box set to *Chosen pages* only finds pages under the ones ticked. |

## A page is missing from a menu

| Check | What to do |
|---|---|
| Is it published? | A Page navigation widget with a parent path leaves out unpublished pages. |
| Is **Show in menu** ticked? | Check the **Properties** tab. |
| Is it a folder? | A Page navigation widget with a parent path does not list folders. |
| Is the menu looking in the right place? | Open the Page navigation widget and check **Parent path for children**. |

## I cannot see something I expect to

| What is missing | Why |
|---|---|
| The **Admin** link, or a tool in the admin area | Your role does not include it. Ask an administrator to check [Roles and access](managing-roles.md). |
| The **Edit** button on a page | You are not signed in as an editor, or the page is not a content page. Wiki pages are edited from **Admin**, then **Pages**. Fixed pages are edited through [content blocks](content-blocks.md). |
| The pencil beside a content block, or the preview of an unpublished page | Both need the content editor role itself. Use **Admin**, then **Content blocks** or **Pages**, or ask for that role as well. |
| A new content block in the list | A block appears after someone has visited the page it is on. Visit the page, then look again. |

## I cannot do something

| Message | What it means |
|---|---|
| A page already exists at that path. | Another page under the same parent has that URL segment. |
| Cannot delete: remove or move its child pages first. | Delete or move the pages beneath it, then try again. |
| Cannot move a page under itself. | You dropped a page onto one of the pages beneath it. |
| Another page already uses that URL. | Choose a different URL segment. |
| The import session expired. Upload the file again. | You waited too long on the Review import screen. Nothing was changed. |
| Search is temporarily unavailable | A fault in the service. Try again in a few minutes, then tell the development team. |

## I made a mistake

| What happened | How to undo it |
|---|---|
| Published something too early | Open the **Versions** tab and select **Publish** on the previous version. |
| Deleted a widget or region | Do not publish. The live version is untouched: open it with **View** on the **Versions** tab and copy back what you need. If you already published, publish the previous version again. |
| Deleted a page | Restore it from **Deleted pages**. |
| Saved a content block with a mistake | Open its **Version history** and select **Revert** on the version before. |
| Imported the wrong file | Import the copy you exported beforehand, choosing **Overwrite**. Without that copy, the versions an overwrite replaced cannot be recovered. |

## A page comes back after I unpublish it

An earlier version was published with no end date, so it became the live version again. Open the **Versions** tab and select **Unpublish** on each version marked *Live* until none is.

## A scheduled page is live but search shows the old words

Search, menus and the **Versions** tab catch up the next time someone publishes or edits the page. Open the **Versions** tab and select **Publish** on the version that should be live.

## My changes to a page have gone

If the editor shows the warning *This page is supplied with the service* on that page, the service imported a newer version of it and overwrote yours. That is expected for the pages of this guide and any others the service supplies. Ask the development team to make the change in the service.

If there is no warning, an administrator can check the [audit log](viewing-logs.md) for a *Content import*.

## The page publishes at the wrong time

Publishing dates are in UTC. In summer the UK is one hour ahead, so a page set to 09:00 goes live at 10am UK time. Enter a time one hour earlier. See [Drafts, publishing and versions](managing-versions.md).

## Still stuck

Tell the development team:

- the address of the page
- what you expected and what happened instead
- the time it happened

An administrator can add what the [audit log](viewing-logs.md) shows for that page.
