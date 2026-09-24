# Sample data share: pupil favourites

A light-hearted data share for demos. It shows what a schema can do with no code change. Each row is
one pupil's favourite band or singer, favourite food and favourite colour.

| File | What it is |
|---|---|
| `pupil-favourites.csv` | The supplier file: 25 pupils. 22 are at the dev sign-in school (860/4070) and 3 are at 933/4290. |
| `pupil-favourites.json` | The schema: it validates each row and decides what the screen and the CSV download show. |

## What it shows

| Supplier column | On screen | In the CSV download |
|---|---|---|
| `SURNAME` | "Last name", 1st, searchable | "Surname", column B |
| `FORENAMES` | "First name", 2nd, searchable | "Forename", column A |
| `YEAR_GROUP` | "Year", 3rd | "Year group", column C |
| `FAV_ARTIST` | "Favourite band or singer", 4th, searchable | "Artist", column F |
| `FAV_FOOD` | "Favourite food", 5th | "Food", column G |
| `FAV_COLOUR` | "Colour", 6th | "Favourite colour (British spelling)", column H |
| `FUN_FACT` | "Fun fact", 7th | **Not exported** (screen only) |
| `CYPMD_ID` | **Hidden** | "CYPMD ID", column D (CSV only) |
| `UPN` | **Hidden** | "UPN", column E (CSV only) |
| `SURVEY_DATE` | **Hidden** | "Date surveyed", column I (CSV only) |
| `CONSENT_REF` | **Hidden** | "Consent form reference", column J (CSV only) |
| `LAESTAB` | Hidden | Not exported. It only splits the file into one file per school. |
| `SUPPLIER_BATCH` | Not in the schema, so dropped when the file is loaded | Not exported |

The schema settings that do this:

- `x-display.label`, `x-display.order` and `x-display.searchable` set the screen label, the position
  and the search.
- `x-display.visible: false` hides a column on screen.
- `x-csv.heading` and `x-csv.columns.default` (a spreadsheet column letter) set the CSV heading and
  position. An empty `columns` map (`{}`) leaves the column out of the CSV.
- `x-display.section` is the title in the dataset list, and `x-download.fileName` is the name of the
  downloaded file.

The schema also validates. `YEAR_GROUP` must be a whole number from 7 to 13. Change one value to `14`
and validate again: the run fails, names the school and the row, and the live data does not change.

## How to use it

1. Put `pupil-favourites.csv` in the ingress storage account, which is where the "Choose CSV" page
   lists files from. Locally this is the `ingressaccount` Azurite account. Any container will do.
2. On a window, add a checking exercise. Choose "No exercise type (display only)", give it a tab
   name such as "Favourites", and choose **Table** under "How schools see the data".
3. On the exercise's Data tab, choose **Add data file**. Name it `pupil-favourites`, leave inclusion
   as "Use the CSV's values", and save.
4. Choose the CSV, then upload `pupil-favourites.json` as the schema.
5. Validate. Sign in as the dev school and open the window: the tab lists 22 pupils.
