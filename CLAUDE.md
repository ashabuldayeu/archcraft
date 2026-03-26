# archcraft

## Project Structure

```
/
├── src/            # Source projects
├── tests/          # Test projects (mirrors src/ structure)
├── specs/
│   ├── active/     # Approved specs currently being implemented
│   ├── done/       # Completed specs
│   └── _template.md
└── CLAUDE.md
```

## Development Workflow — Specification First

**Always follow this order: Specify → Implement → Verify**

### 1. Specify

Use `/spec` to start a new feature. Claude will:
- Ask all clarifying questions upfront (goals, scope, acceptance criteria, domain, constraints)
- Draft a spec document based on answers
- Save it to `specs/active/YYYY-MM-DD_<slug>.md` once approved

**No code is written before a spec is approved.**

### 2. Implement

Work against the saved spec. The acceptance criteria are the definition of done.
Reference the spec file in commits: `feat: user registration (specs/active/2026-03-25_user-registration.md)`.

### 3. Verify

Use `/spec-verify` to compare the implementation against the spec. It will:
- Check each acceptance criterion (met / not met / partial)
- Flag scope drift (code outside the spec)
- Return a PASS / FAIL / REVIEW verdict

On PASS, the spec moves to `specs/done/`.

## Tech Stack

- .NET (C#)
- xUnit for unit and integration tests
- FluentAssertions for readable assertions

## Code Conventions

### General
- Follow Clean Code principles (meaningful names, small focused methods, single responsibility)
- No magic numbers or strings — use named constants
- Prefer composition over inheritance
- No commented-out code

### Naming
- Classes, methods, properties: `PascalCase`
- Local variables, parameters: `camelCase`
- Private fields: `_camelCase`
- Interfaces: `IFoo`
- Async methods: suffix `Async`

### Project Layout (per domain)
```
src/Foo/
├── Foo.csproj
├── Domain/         # Entities, value objects, domain events
├── Application/    # Use cases, interfaces, DTOs
├── Infrastructure/ # Implementations, adapters, persistence
└── Api/            # Entry points (controllers, endpoints)
```

### Tests
- Test projects mirror src structure: `tests/Foo.UnitTests/`, `tests/Foo.IntegrationTests/`
- Test class naming: `FooTests`, `FooShould`
- Test method naming: `MethodName_StateUnderTest_ExpectedBehavior`
- Arrange / Act / Assert sections, separated by blank lines

### Dependencies
- Domain has no external dependencies
- Application depends only on Domain
- Infrastructure and Api depend on Application
- Tests depend only on the layer they test (plus test utilities)

## What NOT to do
- Do not add code that is not required by the current task
- Do not add error handling for scenarios that cannot happen
- Do not create abstractions for single use cases
- Do not use `var` when the type is not obvious from the right-hand side
