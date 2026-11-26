# DocOrganizer

A .NET 10 backend for tracking personal documents with expiry dates: passports, visas, insurance policies, certificates. It keeps an expiry calendar and schedules reminders before each document runs out.

The project focuses on **data correctness end to end**. Rules are enforced in the database schema, in the API validation layer and in the business logic, and concurrency, transactions and query performance are measured rather than assumed.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![EF Core](https://img.shields.io/badge/EF%20Core-10-512BD4)
![SQL Server](https://img.shields.io/badge/SQL%20Server-CC2927)
![OpenAPI](https://img.shields.io/badge/OpenAPI-3.1-6BA539)
![k6](https://img.shields.io/badge/load%20test-k6-7D64FF)

---

## Highlights

- **Layered data integrity.** Rules are checked in three places: SQL Server constraints (UNIQUE, CHECK, FK, rowversion), request validation (DataAnnotations and 17 FluentValidation rules) and domain rules that return `409 Conflict` or `422 Unprocessable Entity`.
- **No lost updates.** Optimistic concurrency uses a SQL Server `rowversion` token. A stale write gets `409` and does not silently overwrite newer data.
- **Measured performance work.** k6 load tests compare a naive and an optimized query path on 5,000+ rows. **p95 latency dropped from 3.1 s to 22 ms** and throughput went up 3×, with 0% errors in both runs.
- **The same data layer written two ways.** Raw ADO.NET with parameterized SQL and explicit transactions sits next to EF Core with LINQ, projections and an N+1 check.
- **Secure REST API.** It uses OAuth 2.0 / OpenID Connect (Auth0, Authorization Code + PKCE), role- and resource-based authorization, RFC 9457 ProblemDetails, pagination, filtering and sorting.
- **Documented and testable.** The repo includes an OpenAPI 3.1 spec, a Scalar UI and a Postman collection of 67 requests with automated assertions.

---

## Repository structure

| Project | Type | What it shows |
|---|---|---|
| [`DocOrganizer.Api`](DocOrganizer.Api) | ASP.NET Core Web API | Production-style REST API: 30 endpoints, auth, validation, caching, load tests |
| [`DocOrganizer.DataLayer`](DocOrganizer.DataLayer) | Console app | ADO.NET and EF Core side by side: CRUD, complex SQL, transactions, concurrency, generated SQL |
| [`DocOrganizer.Scaffolded`](DocOrganizer.Scaffolded) | Console app | Database-first: a DbContext reverse-engineered from the existing schema with `dotnet ef dbcontext scaffold` |

### Domain model

```
Owner 1───* Document *───1 DocumentType
               │  *
               │  └───* Tag        (many-to-many via DocumentTag, with AddedAt)
               └──1───* Reminder
```

---

## Data quality and integrity

### 1. Database level (EF Core Fluent API → SQL Server)

| Rule | Implementation |
|---|---|
| A document number is unique within its document type | Composite `UNIQUE (DocumentTypeId, Number)` |
| One reminder per offset per document | `UNIQUE (DocumentId, DaysBefore)` |
| Unique owner e-mails, type names and tag names | `UNIQUE` indexes |
| The expiry date cannot be before the issue date | `CHECK (ExpiryDate >= IssueDate)` |
| A default reminder offset stays within 1 to 365 days | `CHECK` constraint |
| A type that is in use cannot be deleted | FK with `DeleteBehavior.Restrict` |
| Enums are stored readably | `HasConversion<string>()` with a bounded length |
| Concurrent edits are detected | `rowversion` concurrency token |
| Reference data is seeded consistently | `HasData` for document types and tags, in versioned migrations |

Schema changes go through **EF Core migrations**. Idempotent SQL scripts are generated as well, in [`DocOrganizer.DataLayer/Migrations/Sql`](DocOrganizer.DataLayer/Migrations/Sql).

### 2. API input validation

- **DataAnnotations** check field-level rules: required fields, lengths, ranges and e-mail format.
- **FluentValidation** checks rules that span fields or depend on time. An issue date cannot be in the future, the expiry date must be on or after the issue date, and document numbers must match a format. A global MVC filter applies these rules.
- Invalid input returns **400** with a field-by-field `ValidationProblemDetails` body.

### 3. Business rules → precise HTTP codes

| Situation | Response |
|---|---|
| Duplicate document number or duplicate e-mail | `409 Conflict` |
| Deleting an owner who still has documents | `409 Conflict` |
| Updating a document with a stale `version` | `409 Conflict` |
| A reminder for a document with no expiry date | `422 Unprocessable Entity` |
| A reference to a non-existent type, owner or tag | `422 Unprocessable Entity` |
| A race that slips past the checks (SQL error 2601/2627/547) | Mapped to `409` by the global exception handler |

### 4. Transactions

A document renewal is **atomic**. It creates the new document, carries over the tags, schedules a default reminder and marks the old document `Replaced`, and either all of these steps are saved or none are. The DataLayer project shows commit and rollback twice: once with ADO.NET (`SqlTransaction`) and once with EF Core (`BeginTransactionAsync`).

---

## Performance: before and after

The load test is a [k6 scenario](DocOrganizer.Api/loadtests/documents-load.js) that ramps to 50 virtual users against a user with **5,009 documents**. A config switch, `Performance:Optimized`, toggles between the two implementations of `GET /documents`.

| Metric | Naive | Optimized | Change |
|---|---:|---:|---:|
| Throughput (req/s) | 25.0 | 75.4 | **×3.0** |
| Avg latency | 713 ms | 9.9 ms | **72× faster** |
| p95 latency | 3,147 ms | 22 ms | **143× faster** |
| p99 latency | 8,822 ms | 36 ms | **243× faster** |
| Error rate | 0% | 0% | none |
| Threshold `p95 < 300 ms` | ✗ | ✓ | |

**What changed:**
1. **Filtering, sorting and paging moved into SQL** (`WHERE`, `ORDER BY`, `OFFSET/FETCH`, backed by indexes). The naive version loaded every document with all its `Include`s and filtered them in memory.
2. **`AsNoTracking()` with a projection to DTOs.** EF Core reads only the 8 columns it needs and does not build change-tracking snapshots.
3. **Per-user OutputCache** with eviction on writes.

Raw k6 summaries are in [`loadtests/results-before.json`](DocOrganizer.Api/loadtests/results-before.json) and [`results-after.json`](DocOrganizer.Api/loadtests/results-after.json).

---

## API overview

The base URL is `https://localhost:7042/api/v1`. The full spec is in [`docs/openapi.json`](DocOrganizer.Api/docs/openapi.json).

| Resource | Endpoints |
|---|---|
| `/owners` | CRUD with search and pagination |
| `/documents` | CRUD, filters (owner, type, status, expiry state, tag, text), sorting, paging, `/expiry-calendar`, `/export/{csv\|json}`, `/{id}/renewal` |
| `/documents/{id}/reminders` | Create, list, get and delete |
| `/reminders/due`, `/reminders/{id}/sent` | Reminder dispatch queue (admin only) |
| `/document-types`, `/tags` | Reference data (written by admin only) |
| `/me` | Claims of the current user |

**Roles:** `viewer` can only read, `member` can read and write their own data, and `admin` can access everything. Resource-based authorization uses a custom `IAuthorizationHandler`. Another user's resource returns **403**, and a missing one returns **404**.

**Other features:** URL API versioning, keyed DI services for the exporters, compile-time mapping with Mapperly, response compression, CORS, and ProblemDetails for every error with no stack traces in Production.

---

## Tech stack

**Backend:** C# 14, .NET 10, ASP.NET Core, EF Core 10, ADO.NET (`Microsoft.Data.SqlClient`)
**Database:** SQL Server, migrations, constraints, indexes, `rowversion`
**Security:** OAuth 2.0 / OIDC (Auth0), JWT Bearer, PKCE, policy- and resource-based authorization
**Quality and testing:** FluentValidation, Postman (67 requests with tests), k6 load testing
**Docs:** OpenAPI 3.1, Scalar, XML doc comments

---

## Running locally

### Prerequisites
- .NET 10 SDK
- SQL Server or LocalDB. Connection strings use Windows authentication (`Server=localhost;Trusted_Connection=True`), and you can override them with user secrets.

### DataLayer console demo (no auth required)

```bash
cd DocOrganizer.DataLayer
dotnet run
# Use menu item R to create the database, apply migrations and seed data
```

### Web API

1. **Set up Auth0** (the free tier is enough). See [Auth0 setup](#auth0-setup) below.
2. **Store your values in user secrets** rather than in `appsettings.json`:

   ```bash
   cd DocOrganizer.Api
   dotnet user-secrets set "Oidc:Authority" "https://<your-tenant>.eu.auth0.com/"
   dotnet user-secrets set "Oidc:ClientId"  "<spa-client-id>"
   dotnet user-secrets set "Seed:MemberAccountId" "auth0|<user-id>"
   # optional: Seed:SecondMemberAccountId, Seed:ViewerAccountId
   ```

3. **Run the API:**

   ```bash
   dotnet run --launch-profile https
   ```

   On the first start the API creates the `DocOrganizerApiDb` database, applies migrations and seeds the demo data. Then open **https://localhost:7042/scalar** and click **Authorize**.

### Auth0 setup

1. **APIs → Create API.** Set the identifier to `https://doc-organizer-api` and the algorithm to RS256, then enable RBAC.
2. **Applications → Create Application** of type *Single Page Application*. Add these callback URLs: `https://oauth.pstmn.io/v1/callback, https://localhost:7042/scalar`.
3. **User Management → Roles.** Create `admin`, `member` and `viewer`, then assign them to your users.
4. **Actions → Post-Login.** Add the code below so roles and e-mail end up in the access token:

   ```js
   exports.onExecutePostLogin = async (event, api) => {
     const ns = 'https://doc-organizer';
     api.accessToken.setCustomClaim(`${ns}/roles`, event.authorization?.roles ?? []);
     api.accessToken.setCustomClaim(`${ns}/email`, event.user.email);
   };
   ```

### Postman and load tests

- Import [`postman/DocOrganizer.postman_collection.json`](DocOrganizer.Api/postman/DocOrganizer.postman_collection.json), set the `auth0Domain` and `clientId` collection variables, and run the collection.
- To run the load test: `k6 run -e TOKEN=<access_token> DocOrganizer.Api/loadtests/documents-load.js`

---

## Possible improvements

- Integration tests with Testcontainers (SQL Server) and a CI pipeline
- Data-quality monitoring, for example a report of documents with missing expiry dates or orphaned reminders
- Real reminder delivery through a background worker reading the `/reminders/due` queue
- A distributed cache such as Redis for multi-instance deployments
