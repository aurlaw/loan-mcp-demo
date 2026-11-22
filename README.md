# Loans MCP Demo

Proof-of-concept commercial-loans web app (ASP.NET Core Razor Pages, .NET 10) that will expose an
OAuth-protected MCP endpoint for Claude/ChatGPT.

## First-time setup

The app seeds demo users on startup (Development) and **will not start without a seed password**:

```
dotnet user-secrets set "Seed:DefaultPassword" "<demo password>" --project src/Loans.Web
```

The password must meet ASP.NET Identity's default rules: at least 6 characters, with an uppercase
letter, a lowercase letter, a digit, and a symbol (e.g. `Demo-Pass1!`). All seeded users share it:
`borrower@loans.test`, `officer@loans.test`, `underwriter@loans.test`, `admin@loans.test`.

The password is only applied when users are first created. To change it later, reset the database
(see below).

## Commands

- Run: `dotnet run --project src/Loans.Web` — then open http://localhost:5080 (health check at `/health`)
- Build: `dotnet build`
- Test: `dotnet test`
- Reset demo data: delete `src/Loans.Web/loans.db`, or run once with
  `Database__ResetOnStartup=true dotnet run --project src/Loans.Web`

See `AGENTS.md` for conventions.
