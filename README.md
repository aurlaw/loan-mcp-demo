# Loans MCP Demo

Proof-of-concept commercial-loans web app (ASP.NET Core Razor Pages, .NET 10) that will expose an
OAuth-protected MCP endpoint for Claude.

- Run: `dotnet run --project src/Loans.Web` — then open http://localhost:5080 (health check at `/health`)
- Build: `dotnet build`
- Test: `dotnet test`

See `AGENTS.md` for conventions.
