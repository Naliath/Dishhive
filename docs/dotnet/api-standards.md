# .NET Web API Standards

## Controller Best Practices
- Use attribute routing with [Route("api/[controller]")]
- Return IActionResult or ActionResult<T>
- Implement proper HTTP status codes (200, 201, 204, 400, 404, 500)
- Add XML documentation comments for OpenAPI documentation
- Use DTOs for request/response models (never expose entities directly)
- Follow RESTful conventions (GET, POST, PUT, DELETE)

## Entity Framework Core
- Use async methods (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync)
- Implement proper indexes in migrations for query performance
- Use Include for eager loading related data
- Avoid N+1 query problems (use projection or eager loading)
- Use transactions for multi-step operations
- Use AsNoTracking for read-only queries
- Do not generate database migration scripts, change the model and use "dotnet ef migrations add {migration-name}" to add the migration files

## Error Handling
- Use global exception handling middleware
- Return consistent error response format
- Log errors with structured logging (ILogger)
- Don't expose internal details in production
- Validate inputs and return 400 BadRequest with details
- Use try-catch for specific exceptions only

## Security
- Validate all inputs (use DataAnnotations)
- Use parameterized queries (EF does this automatically)
- Implement CORS properly (don't use AllowAny in production)
- Add rate limiting for production
- Use HTTPS in production
- Don't log sensitive data

## Performance
- Use AsNoTracking for read-only queries
- Implement pagination for large datasets
- Cache frequently accessed data (IMemoryCache)
- Use compiled queries for hot paths
- Profile queries with EF logging
- Use connection pooling (default in EF Core)

## API Response Patterns
```csharp
// GET - Return collection
[HttpGet]
public async Task<ActionResult<IEnumerable<ItemDto>>> GetItems()
{
    var items = await _context.Items
        .AsNoTracking()
        .Select(i => new ItemDto { ... })
        .ToListAsync();
    return Ok(items);
}

// GET by ID - Return single item
[HttpGet("{id}")]
public async Task<ActionResult<ItemDto>> GetItem(int id)
{
    var item = await _context.Items.FindAsync(id);
    if (item == null)
        return NotFound();
    return Ok(new ItemDto { ... });
}

// POST - Create new resource
[HttpPost]
public async Task<ActionResult<ItemDto>> CreateItem(CreateItemDto dto)
{
    var item = new Item { ... };
    _context.Items.Add(item);
    await _context.SaveChangesAsync();
    return CreatedAtAction(nameof(GetItem), new { id = item.Id }, new ItemDto { ... });
}

// PUT - Update existing resource
[HttpPut("{id}")]
public async Task<IActionResult> UpdateItem(int id, UpdateItemDto dto)
{
    var item = await _context.Items.FindAsync(id);
    if (item == null)
        return NotFound();
    
    // Update properties
    await _context.SaveChangesAsync();
    return NoContent();
}

// DELETE - Remove resource
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteItem(int id)
{
    var item = await _context.Items.FindAsync(id);
    if (item == null)
        return NotFound();
    
    _context.Items.Remove(item);
    await _context.SaveChangesAsync();
    return NoContent();
}
```

## DTO Patterns
- Create separate DTOs for Create, Update, and Read operations
- Use separate files for DTOs that are not expressly related
- Use manual mapping no Automapper
- Never expose EF navigation properties in DTOs
- Validate DTOs with DataAnnotations

## Testing
- Write unit tests for business logic
- Use InMemory database for integration tests
- Mock external services
- Test error handling paths
- Aim for >70% code coverage
