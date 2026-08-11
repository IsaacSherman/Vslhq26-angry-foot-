# Resume parsing corpus

Golden-file regression suite for `ResumeBulletParser`, driven by `ResumeCorpusTests`.

To add a case — no code change required:

1. Drop the raw pasted resume in as `ResumeN.txt` (next free `N`).
2. Drop the bullets it should produce in as `BulletsN.txt`, one bullet per line, in order,
   already stripped of markers and with wrapped lines joined.
3. Run the tests.

When a real resume parses badly, save it here first so the fix is pinned by a test. Blank lines in
`BulletsN.txt` are ignored, so the expected output can be spaced out for readability. A `ResumeN.txt`
with no matching `BulletsN.txt` fails the suite rather than being skipped.
