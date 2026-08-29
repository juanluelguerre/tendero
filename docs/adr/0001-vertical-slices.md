# 0001 — Vertical slices with Carter + custom CQRS

**Status**: accepted

## Context
The codebase must stay navigable as features multiply, and each feature is
also a blog artifact that should read end to end.

## Decision
One folder per feature (`Features/<Name>`) containing endpoint (Carter),
command/query, validator (FluentValidation), and handler. Custom
`ICommand`/`IQuery` dispatcher abstractions instead of the MediatR package.
Slices never reference each other; sharing goes down (SharedKernel) or out
(ports). Reference: `Catalog/Features/ImportProducts`.

## Consequences
Adding a feature touches one folder. Architecture tests enforce slice
isolation. Cross-cutting behavior lives in dispatcher middleware.
