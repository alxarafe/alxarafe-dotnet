# ADR 0002: Native platform capabilities

## Decision

Alxarafe.NET uses ASP.NET Core Identity primitives for users and password handling, the built-in Identity bearer token handler for API authentication, ASP.NET Core authorization policies for permissions, `RequestLocalization` and `IStringLocalizer` for `es`/`en`, `AddProblemDetails` with `IExceptionHandler` for consistent errors, and the first-party `Microsoft.AspNetCore.OpenApi` package for the OpenAPI document.

Only `/api/auth/register` and `/api/auth/login` are exposed in this iteration. The endpoints use `UserManager` and `SignInManager`; they do not implement password hashing, token signing, or token validation. Recovery, MFA, email, and social login remain deliberately out of scope.

## Permission modularity

ASP.NET Core owns authorization. Alxarafe adds only a small shared `IPermissionDefinitionProvider` convention so a module can publish names such as `module.resource.read`. A policy provider converts those declared names into native `IAuthorizationRequirement` policies, and a handler checks standard `permission` claims. This is modular integration rather than a parallel authorization engine.

## Localization and errors

Domain and Application never depend on localized text. HTTP resources live with the relevant adapter/module. I18N-001 supersedes the original Spanish default: `Accept-Language` now selects English or Spanish with English as the base/fallback, using only the native header provider. Responses retain native RFC-compatible ProblemDetails and their stable technical extensions. See [runtime internationalization](../i18n.md).

## Testing and operations

Development/test seed users are created only in Development or Testing. Bruno complements unit, architecture, and `WebApplicationFactory` integration tests. Docker Compose remains the only required host tooling; PostgreSQL runs in containers and security data uses a separate development database.
