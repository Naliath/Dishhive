# Dishhive Testing Strategy

**Created:** April 19, 2026  
**Status:** 🚧 In Progress

## Executive Summary

This document outlines the testing strategy for Dishhive, covering both .NET API backend and Angular frontend. The approach mirrors Freezy's proven strategy to maintain ecosystem consistency.

### Golden Rule
**Never modify a test to make failing code pass.** Tests are the contract. Fix the code, not the test.

### Test Coverage Target

| Component | Target | Status |
|-----------|--------|--------|
| Backend — Service Tests | ≥ 10 | 🚧 In Progress |
| Backend — Integration Tests | ≥ 15 | 🚧 In Progress |
| Frontend — Service Tests | ≥ 50 | ⬜ Not Started |
| **Total** | **≥ 75** | **🚧 In Progress** |

---

## 1. Backend Testing (.NET API)

### 1.1 Test Project Structure

**Location:** `src/Dishhive.Api.Tests/`

**Frameworks & Libraries:**
- xUnit 2.9.x — Test framework
- FluentAssertions 8.x — Readable assertions
- NSubstitute 5.x — Mocking framework
- Microsoft.AspNetCore.Mvc.Testing 10.x — Integration testing
- Microsoft.EntityFrameworkCore.InMemory 10.x — In-memory database for tests
- coverlet.collector 6.x — Code coverage

**Project Reference:** Added to `Dishhive.sln`

### 1.2 Test Infrastructure

#### Test Data Builders (`Builders/`)
- **RecipeBuilder.cs**: Fluent API for creating test recipe entities
  - Example: `new RecipeBuilder().WithTitle("Spaghetti").WithServings(4).Build()`
- **FamilyMemberBuilder.cs**: Fluent API for family member test data
- **WeekPlanBuilder.cs**: Fluent API for week plan test data

#### Test Base Classes
- **TestWebApplicationFactory.cs**: Custom `WebApplicationFactory<Program>`
  - Uses in-memory EF Core database
  - Skips PostgreSQL setup (via `Testing` environment)
  - Unique database per factory instance for test isolation

- **TestBase.cs**: Base class for integration tests
  - Provides: Factory, HttpClient, DbContext, Scope
  - Includes `ClearDatabase()` for test isolation
  - Includes `CreateFreshContext()` to avoid EF change tracker issues

### 1.3 Test Categories

#### Service Tests (`Services/`)
- **RecipeImportServiceTests.cs** ← highest-priority initial tests
  - Import from valid Dagelijkse Kost URL
  - Expected fields: title, description, ingredients, steps, servings, picture, video link, source link
  - Handle invalid URL
  - Handle HTTP error response
  - Handle malformed HTML

#### Integration Tests (`Integration/`)
- **RecipesControllerIntegrationTests.cs**
  - CRUD operations
  - Import endpoint
- **FamilyControllerIntegrationTests.cs**
  - Member CRUD
  - Preference management
- **WeekPlannerControllerIntegrationTests.cs**
  - Plan assignment
  - Retrieval by week

### 1.4 Test Execution

```powershell
# Run all tests
dotnet test

# Run specific category
dotnet test --filter "FullyQualifiedName~Services"
dotnet test --filter "FullyQualifiedName~Integration"

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

---

## 2. Frontend Testing (Angular)

### 2.1 Test Framework & Infrastructure

**Test Framework:** Vitest (via `@angular/build:unit-test`)  
**DOM Implementation:** jsdom  

**Location:** `src/dishhive-web/src/app/`

### 2.2 Test Infrastructure

#### Test Utilities (`src/app/testing/`)

**test-utils.ts:**
- `MockLocalStorage` — In-memory localStorage
- `setupMockLocalStorage()` — Setup helper
- `createMockMediaQuery()` — Mock matchMedia

**test-builders.ts:**
- `RecipeBuilder` — Fluent API for recipe test data
- `FamilyMemberBuilder` — Fluent API for family member test data
- `WeekPlanBuilder` — Fluent API for week plan test data

### 2.3 Service Test Coverage Target

| Service | Target Tests | Status |
|---------|-------------|--------|
| recipes.service | 15 | ⬜ Not Started |
| family.service | 12 | ⬜ Not Started |
| week-planner.service | 15 | ⬜ Not Started |
| settings.service | 10 | ⬜ Not Started |
| theme.service | 10 | ⬜ Not Started |

### 2.4 Test Pattern

```typescript
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

describe('RecipesService', () => {
  let service: RecipesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        RecipesService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(RecipesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should fetch all recipes', () => {
    const mockRecipes = [RecipeBuilder.create().build()];
    service.getRecipes().subscribe(recipes => {
      expect(recipes.length).toBe(1);
    });
    const req = httpMock.expectOne('/api/recipes');
    expect(req.request.method).toBe('GET');
    req.flush(mockRecipes);
  });
});
```

---

## 3. Recipe Import Testing (Priority)

The recipe import feature requires at minimum one automated test that validates extraction from a known source.

**Approach:** Use a saved HTML snapshot of a Dagelijkse Kost recipe page as a fixture. This avoids network dependencies in CI and makes tests deterministic.

**Test file:** `src/Dishhive.Api.Tests/Services/RecipeImportServiceTests.cs`

**Minimum assertions per import test:**
- `[ ]` Title extracted
- `[ ]` Description extracted (or null if not present)
- `[ ]` At least one ingredient extracted
- `[ ]` At least one preparation step extracted
- `[ ]` Serving count extracted (or null)
- `[ ]` Picture URL extracted (or null)
- `[ ]` Source link matches input URL

See `docs/features/06-recipe-import.md` for full research and architecture details.
