# Authorization: design note

> ## NOT IMPLEMENTED
>
> **Every endpoint in this service is currently anonymous.** There is no authentication, no
> authorization, no token endpoint, no user store, and no role checking anywhere in the code.
>
> This document describes what *would* be built and why. Nothing in it is a statement about the
> artifact. Every code sample is illustrative: none of it compiles today, and none of it is
> commented-out real code waiting to be switched on.
>
> The seams are marked in `src/LibraryLoans.Api/Program.cs` and
> `src/LibraryLoans.Api/Books/BooksEndpoints.cs` so the omission is visible where it lives rather
> than only in a document.

## Why it is not built

The brief asks for a scalable CRUD API with cross-cutting concerns, domain rules enforced in the
system, validation, observability, and tests. It does not ask for authentication.

Given a fixed budget, the choice was between a working authentication system and the domain
invariants that the brief *does* name, including its own example, that the same book cannot be
loaned twice. Enforcing an invariant correctly under concurrency is the harder problem and the one
being assessed. Auth is well-understood, and a partially-built one demonstrates less than a
documented decision not to build it.

Stating that plainly is the point. An unexplained absence looks like something forgotten; this is
a trade with a stated reason.

## What would be used: an external identity provider

**Microsoft Entra ID**, or any OIDC-compliant provider, validated as a JWT bearer token. Not a
hand-rolled login.

That is the whole recommendation, and the reasoning is that authentication is not one feature. It
is password storage, credential rotation, brute-force lockout, multi-factor enrolment, session
revocation, token refresh, audit logging, and a recovery flow for people who lose access. Each is
a place to be subtly wrong, several have regulatory weight at a bank, and none of them are
differentiating work. A provider that already does all of it, correctly, is the right answer.

The property that matters most operationally: with an external provider this service holds **no
signing key**. It validates tokens against the provider's published JWKS endpoint. There is no
secret in configuration to leak, rotate, or accidentally commit, which is a materially different
security posture from symmetric HS256 signing, where the service that validates tokens can also
mint them.

## Default-deny, and why it is the one line that matters

```
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

Every endpoint requires an authenticated caller unless it explicitly opts out. A new endpoint
added under deadline pressure, with no authorization attribute, returns **401**, not data.

The inverse arrangement is the classic failure: middleware that inspects an endpoint for a
permission rule and calls `next()` when it finds none. That is fail-open, and it fails *silently*,
which is the worst pairing available: the system appears to work, tests pass, and the hole is
found by someone who was looking for it.

Only two endpoints would opt out, both health probes, because an orchestrator has no token to
present.

## Roles and permissions are different questions

Conflating them is how authorization logic ends up duplicated and inconsistent.

**Role rules**: coarse, claim-based, answerable from the token alone. These belong in policies:

| Policy | Applies to |
|---|---|
| `RequireLibrarian` | writing to the catalogue; adding and retiring copies; managing members; acting on any member's loans |
| `RequireMember` | borrowing and returning as oneself; reading the catalogue |

**Permission rules**: resource-scoped, *not* answerable from claims. "May this caller act on
**this** member's loans?" depends on which member the request names. That check belongs in the
handler, alongside the other rules about what is allowed, because:

- it needs the resource, which the policy layer has not loaded yet;
- it is a business rule (*a member acts only for themselves, a librarian acts for anyone*), and
  business rules living in one place is the reason this codebase has an Application layer;
- put in a policy per endpoint, it gets written slightly differently the third time.

The concrete rule: a `member` caller may borrow or return only where the token's subject maps to
the `memberId` in the request. A `librarian` caller has no such restriction, because a real
library accepts a returned book from whoever walks in.

## What would not change

The `Domain` and `Application` projects. No aggregate learns what a role is; no value object gains
a claims check. `Loan.Open` cares whether the member is suspended and under their limit, which are
domain rules, and not who authenticated the HTTP request, which is not.

That is the dependency rule producing a concrete benefit rather than being asserted. Authorization
is a delivery-mechanism concern, so it lives in the delivery mechanism, and the parts of the system
that would be expensive to change are untouched.

## How it would be tested

Two integration tests carry most of the value, and they test the *posture* rather than the
mechanism:

1. an unauthenticated request to any non-health endpoint returns **401**, which proves default-deny
   is actually in force, not merely configured;
2. an authenticated caller with the wrong role returns **403**, which proves the policies are
   attached.

Both would use a test authentication handler rather than real tokens, so the suite has no
dependency on an identity provider being reachable.

## What production would add beyond this

- Short token lifetimes with refresh, rather than long-lived bearer tokens.
- Rate limiting on write endpoints, and on any future token endpoint, independent of authorization.
- Audit logging of who performed each state change: a bank requirement, and one that argues for
  capturing the caller's subject on the aggregate rather than only in a log line.
- No personal data in logs. Identifiers only; see the logging rule in
  [ARCHITECTURE.md](ARCHITECTURE.md).
