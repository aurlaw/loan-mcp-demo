# AGENTS.md

## Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Run: `dotnet run --project src/Loans.Web` (http://localhost:5080)

## Projects
- `Loans.Core` — domain, engine, data. `Loans.Mcp` — MCP tools/prompts. `Loans.Web` — host. `tests/` — xUnit v3.

## Rules
- Money is integer cents; rates are integer basis points; DSCR is ratio × 100. No floating point for money.
- `Loans.Core.Engine` is pure: no `DbContext`, no `Microsoft.EntityFrameworkCore`, no ASP.NET.
- Errors flow through `Result`/`Result<T>`; don't throw for expected failures.
- External dependencies sit behind interfaces, registered via DI.
- Packages are centrally versioned in `Directory.Packages.props`; no prereleases; no new packages without being asked.
- No Aspire.
- Agents author code/config and may run installs, builds, and tests. Never run git, deploy, or change infrastructure.
