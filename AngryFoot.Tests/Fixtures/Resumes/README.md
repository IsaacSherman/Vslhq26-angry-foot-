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

The `.docx` files here are the sources some fixtures were captured from, kept for the PDF/DOCX import
work tracked in #14.
