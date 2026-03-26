# Spec: [Feature Name]

**Status:** draft | approved | in-progress | done
**Created:** YYYY-MM-DD
**Author:** [name]

---

## Summary

One paragraph. What is this feature and why does it exist?

## Problem Statement

What problem does this solve? Who has this problem?

## Goals

- [ ] Must-have goal 1
- [ ] Must-have goal 2

## Non-Goals (Out of Scope)

- Explicitly excluded concern 1
- Explicitly excluded concern 2

## Acceptance Criteria

Concrete, verifiable conditions that define "done".

- [ ] AC-1: Given X, when Y, then Z
- [ ] AC-2: ...

## Domain & Architecture

Which domain(s) are involved? Which layers change?

```
src/
└── Foo/
    ├── Domain/     # new: FooEntity, FooCreatedEvent
    ├── Application/# new: CreateFooCommand, IFooRepository
    └── Infrastructure/ # new: FooRepository
```

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| ...      | ...    | ...       |

## Open Questions

- [ ] Q1: ...
- [ ] Q2: ...

## Dependencies

External systems, packages, or other specs this depends on.

---

*Approved by: — Date: —*
