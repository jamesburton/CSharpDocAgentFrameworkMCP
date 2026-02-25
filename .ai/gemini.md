# Guidance for Gemini (coding assistant)

## Goal
Implement DocAgentFramework as specified in `docs/Plan.md`.

## Key priorities
- Clean architecture boundaries
- Solid test coverage
- Small, reviewable changes

## Suggestions
- When implementing parsers, use property-based or table-driven tests.
- Prefer immutable records for domain objects.
- Ensure serialization is stable and versioned.
