# How the journey rules work

A journey is one JSON file in `src/DfE.CheckPerformanceData.Web/Data/QuestionFlows/`. The pages, questions and options are in the file. A **rule** decides one of three things.

| Where you put the rule | What it controls |
|---|---|
| `visibleWhen` on an **option** | Whether the option shows |
| `visibleWhen` on a **question** | Whether the whole question shows |
| `optionalWhen` on a **question** | Whether the user can leave the question blank |
| `requireAtLeastOneWhen` on a **page** | Whether the "fill in at least one" rule is on |

If there is no rule, the option or question always shows, and the question is mandatory unless it says `"optional": true`.

## The three kinds of test

A rule is a list of tests. **Every test in the list must be true.**

### 1. A fact test

A fact test asks something about the pupil, the school or the qualification. These things are not answers in the journey.

```json
{ "fact": "school.isIndependent", "is": false }
```

This means "the school is not independent". You must always write `"is"`.

There are only four facts:

| Fact | True when |
|---|---|
| `pupil.isAddBack` | DfE added the pupil back into the results (inclusion flag 403) |
| `school.isIndependent` | The school is an "Other Independent School" (GIAS type 11) |
| `school.isOnNotOnRollList` | The school is on the not-on-roll college list |
| `qualification.hasSyllabusCodes` | The selected qualification has syllabus codes |

### 2. An answer test

An answer test asks what the user answered earlier in the journey.

```json
{ "answer": "reason", "in": ["english-not-first-language"] }
```

This means "the answer to the question `reason` is `english-not-first-language`". Put more values in the list to mean "any of these answers".

### 3. An any-of test

An any-of test is true when **one or more** of the tests inside it are true. Use it for "or".

```json
{ "anyOf": [
  { "fact": "school.isIndependent", "is": true },
  { "fact": "school.isOnNotOnRollList", "is": true }
] }
```

## A full example

The "Not on roll" option on the KS4 June removal reason page:

```json
"visibleWhen": [
  { "fact": "pupil.isAddBack", "is": false },
  { "anyOf": [
    { "fact": "school.isIndependent", "is": true },
    { "fact": "school.isOnNotOnRollList", "is": true }
  ] }
]
```

In plain English: show this option when the pupil was **not** added back, **and** the school is independent **or** is on the college list.

## How to make common changes

**Hide an option from add-back pupils.** Add this to the option:

```json
"visibleWhen": [{ "fact": "pupil.isAddBack", "is": false }]
```

**Show an option to independent schools only:**

```json
"visibleWhen": [{ "fact": "school.isIndependent", "is": true }]
```

**Add a second condition.** Add another test to the list. Both must be true:

```json
"visibleWhen": [
  { "fact": "pupil.isAddBack", "is": false },
  { "fact": "school.isIndependent", "is": false }
]
```

**Make a question optional after a certain answer:**

```json
"optionalWhen": [{ "answer": "first-language", "in": ["other", "not-known"] }]
```

**Remove a rule.** Delete the `visibleWhen` (or `optionalWhen`) line. The option or question then always shows, or is always mandatory.

You do not need to change C# code for any of these changes. Edit the JSON, then run the app.

## When you need code

You need code only for a **new fact**: something about the pupil, school or qualification that the journey does not ask about. To add one:

1. Write a small class in `Application/Journey/Conditions/` that returns true or false.
2. Register it in `Application/DependencyManager.cs`.
3. Add it to the facts table in `docs/question-flows.md`.

Do not write a class for "not X" or "X or Y". Use `"is": false` or `anyOf` in the JSON.

## What protects you from mistakes

- **A badly written test stops the file from loading.** For example, a plain string, a missing `"is"`, or an `anyOf` with only one test. The error tells you what to write.
- **The rules fail closed.** If the app cannot decide, it takes the safe path:
  - A fact name with no class hides the option.
  - A question that the user has not answered yet does not make a later question optional.
- **The unit tests check every flow file:**
  - Each fact has a class.
  - Each answer test names a real question in the same file.
  - Each value is one of that question's options.

  A typo in a fact name, a question id or an option value fails the build.

The tests check that the rules are valid, but not that they are the rules you want. After a change, go through the journey by hand in the browser. Use the right kind of school and pupil, for example a type 11 school or an add-back pupil, and check that the options show as you expect.
