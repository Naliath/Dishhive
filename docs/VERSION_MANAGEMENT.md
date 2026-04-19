# Version Management

This project uses a **centralized version management** system where `package.json` serves as the single source of truth for the application version across both frontend and backend. This matches the approach used by Freezy.

## How It Works

### Single Source of Truth
The version is defined once in [`package.json`](../src/dishhive-web/package.json):
```json
{
  "version": "0.1.0"
}
```

### Automatic Synchronization

The [`scripts/sync-version.js`](../src/dishhive-web/scripts/sync-version.js) script reads the version from `package.json` and automatically updates:

#### Frontend
1. **TypeScript**: Generates `src/environments/version.ts` (auto-generated, gitignored)
2. **Environment Files**: Import the version from `version.ts`

#### Backend (.NET API)
1. **Project File**: Updates `Dishhive.Api.csproj` with Version, AssemblyVersion, and FileVersion
2. **C# Class**: Generates `AppVersion.cs` (auto-generated, gitignored)
3. **Swagger/OpenAPI**: Uses `AppVersion.Version`

## Updating the Version

1. **Update package.json only:**
   ```bash
   npm version patch  # 0.1.0 -> 0.1.1
   npm version minor  # 0.1.0 -> 0.2.0
   npm version major  # 0.1.0 -> 1.0.0
   ```

2. **Build the application:**
   ```bash
   npm run build
   ```
   The build script automatically syncs the version.

## Manual Sync

```bash
npm run sync-version
```

## Files Affected

### Frontend
- **Source**: `package.json` — **Single source of truth**
- **Build Script**: `scripts/sync-version.js`
- **Generated**: `src/environments/version.ts` (gitignored)
- **Environment**: `src/environments/environment.ts`, `src/environments/environment.prod.ts`

### Backend
- **Project File**: `Dishhive.Api.csproj` — Version, AssemblyVersion, FileVersion
- **Generated**: `AppVersion.cs` (gitignored)
- **Swagger**: `Program.cs` uses `AppVersion.Version`
