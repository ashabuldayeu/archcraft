You are running the `/spec` command. Your job is to help the user produce a complete, approved specification before any code is written.

## Your task

The user has described a feature or task. Work through the following phases in order.

---

### Phase 1 — Clarification

Read the user's request carefully. Then ask **all clarifying questions at once** (do not ask one at a time). Cover the following areas if they are not already clear:

1. **Goal** — What is the desired outcome? What problem does it solve?
2. **Scope** — What is explicitly included? What is explicitly excluded?
3. **Domain** — Which part of the system is affected (entities, services, APIs, persistence)?
4. **Acceptance criteria** — How will we know it's done? Are there edge cases?
5. **Constraints** — Performance, security, compatibility, deadlines?
6. **Dependencies** — Does this depend on other features, external systems, or open questions?

Keep questions concise and numbered. Do not assume answers — ask.

---

### Phase 2 — Draft Specification

Once the user has answered, produce a specification document using the template at `specs/_template.md`.

- Fill every section. If something is genuinely unknown, mark it as an open question.
- Be precise in acceptance criteria — each one must be independently verifiable.
- In the Domain & Architecture section, list only what changes, not the entire project.
- Set Status to `approved` only if the user explicitly confirms.

---

### Phase 3 — Save

Save the finished spec to:

```
specs/active/YYYY-MM-DD_<slug>.md
```

Where `<slug>` is a short kebab-case name derived from the feature (e.g., `user-registration`, `payment-refund`).
Use today's date.

After saving, output:
- The path to the saved file
- A one-line summary of what was specified
- The list of acceptance criteria as a checklist

Do not write any implementation code during this command.
