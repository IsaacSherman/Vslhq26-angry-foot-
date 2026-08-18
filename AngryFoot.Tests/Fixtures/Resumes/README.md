# Resume parsing corpus

Golden-file regression suite for `ResumeBulletParser`, driven by `ResumeCorpusTests`.

Each case is three files sharing a number:

| File | Contents |
| --- | --- |
| `ResumeN.txt` | The raw pasted resume. |
| `BulletsN.txt` | The bullets it should produce, one per line, in order, markers stripped and wrapped lines joined. |
| `EmployersN.txt` | One `employer<TAB>bullet` pair per line, same bullets in the same order. |

Use `(none)` as the employer when the resume names only a position and no employer for it — for
example a run of military assignments listed as bare job titles. Suggesting a job title as though it
were a company is worse than leaving the field blank for the user to fill in, so that case is worth
pinning.

`EmployersN.txt` restates the bullets so each pairing is readable on its own line. The harness checks
that its bullet column matches `BulletsN.txt` exactly and fails loudly if the two drift apart, and a
`ResumeN.txt` missing either companion file fails rather than being skipped.

To add a case: drop in the three files and run the tests. When a real resume parses badly, save it
here first so the fix is pinned by a test. Blank lines in `BulletsN.txt` are ignored, so the expected
output can be spaced out for readability.

Anonymize before committing — replace names, employers, schools, contact details, and figures with
placeholders, but keep the layout (markers, wrapping, tabs, blank lines, section order) exactly as it
came out of the original. The layout is the thing under test.

## Converted documents

`AnonymizedResumeStandard2.docx` is the source of `Resume7.txt`, and `AnonymizedResumeFromPdf3.docx` of
`Resume8.txt`. Each also has a `ResumeN.md`: the same document as markitdown converts it, checked
against the *same* `BulletsN.txt` and `EmployersN.txt`. That shared expectation is the point - it is
what pins "uploading a document yields what pasting its text yields" - so never give a `.md` case its
own expectation files.

The `.md` files are committed rather than converted during the run because the test suite must never
need Docker. To regenerate one, start the container and run the smoke test that produced it:

```bash
docker run --rm -p 3001:3001 mcp/markitdown --http --host 0.0.0.0 --port 3001
```

```bash
RUN_MARKITDOWN_INTEGRATION=1 dotnet test -- --filter-class "*MarkitdownSmokeTests*"
```

A regenerated `.md` that no longer parses to its `BulletsN.txt` is a real finding: either markitdown's
output changed or the parser did, and the corpus is what says which.
