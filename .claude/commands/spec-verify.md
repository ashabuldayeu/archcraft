You are running the `/spec-verify` command. Your job is to compare the current implementation against an approved specification and report the result honestly.

## Your task

### Step 1 — Find the spec

If the user named a spec file, use it. Otherwise:
- List all files in `specs/active/`
- If there is exactly one, use it
- If there are multiple, ask the user which one to verify against

Read the spec file fully before proceeding.

### Step 2 — Gather implementation evidence

Based on the spec's Domain & Architecture section, locate the relevant source files in `src/` and `tests/`. Read them. Do not guess — only report what you can confirm by reading the code.

### Step 3 — Produce a verification report

Output a structured report with the following sections:

---

## Spec Verification Report

**Spec:** `specs/active/<filename>`
**Verified on:** YYYY-MM-DD

### Acceptance Criteria

For each AC item in the spec, mark it:
- ✅ **Met** — with a pointer to the file/line that satisfies it
- ❌ **Not met** — describe what is missing
- ⚠️ **Partial** — describe what exists and what is missing
- ❓ **Cannot determine** — explain why (e.g., no relevant code found)

### Scope Drift

List anything implemented that is **not in the spec** (over-engineering, added features, extra layers). Be factual, not judgemental.

### Open Questions from Spec

For each open question in the spec, report whether it has been resolved in the code, or is still open.

### Verdict

One of:
- **PASS** — all acceptance criteria met, no significant drift
- **FAIL** — one or more acceptance criteria not met (list them)
- **REVIEW** — criteria met but drift or unresolved questions require discussion

---

### Step 4 — On PASS

If verdict is PASS, ask the user whether to move the spec from `specs/active/` to `specs/done/` and update its Status field to `done`.

Do not modify any source code during this command.
