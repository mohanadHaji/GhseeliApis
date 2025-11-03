# GhseeliApis.Tests

This is the test project for GhseeliApis. It contains unit tests for all controllers using xUnit, FluentAssertions, and Entity Framework Core In-Memory Database.

## ?? Test Framework & Libraries

- **xUnit** - Testing framework
- **FluentAssertions** - Fluent assertion library for better test readability
- **Moq** - Mocking framework (for future use with repository pattern)
- **EntityFrameworkCore.InMemory** - In-memory database for testing

## ?? Test Structure

```
GhseeliApis.Tests/
??? Controllers/
?   ??? UsersControllerTests.cs      - Tests for user CRUD operations
?   ??? HealthControllerTests.cs     - Tests for health check endpoints
?   ??? HelloControllerTests.cs      - Tests for hello world endpoint
??? GhseeliApis.Tests.csproj
```

## ?? Running Tests

### Run all tests
```bash
dotnet test
```

### Run tests with detailed output
```bash
dotnet test --verbosity detailed
```

### Run tests with coverage (requires coverlet)
```bash
dotnet test /p:CollectCoverage=true
```

### Run specific test class
```bash
dotnet test --filter "FullyQualifiedName~UsersControllerTests"
```

### Run specific test method
```bash
dotnet test --filter "FullyQualifiedName~GetAllUsers_ReturnsAllUsers_WhenUsersExist"
```

## ?? Test Coverage

### UsersControllerTests (17 tests)
- ? GetAllUsers - Empty list and multiple users scenarios
- ? GetUserById - Not found and successful retrieval
- ? CreateUser - Creation, timestamp validation
- ? UpdateUser - Not found, successful update, persistence
- ? DeleteUser - Not found, successful deletion, isolation

### HealthControllerTests (12 tests)
- ? CheckApiHealth - Status, service name, version, timestamp
- ? CheckDatabaseHealth - Connection, status, error handling

### HelloControllerTests (5 tests)
- ? GetHelloWorld - Response type, status code, message validation

**Total: 34 tests**

## ?? Testing Approach

### In-Memory Database
Each test uses a unique in-memory database instance to ensure test isolation:
```csharp
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
    .Options;
```

### Arrange-Act-Assert Pattern
All tests follow the AAA pattern for clarity:
```csharp
[Fact]
public async Task GetUserById_ReturnsUser_WhenUserExists()
{
    // Arrange - Set up test data
    var testUser = new User { Name = "Test", Email = "test@example.com" };
    _context.Users.Add(testUser);
    await _context.SaveChangesAsync();

    // Act - Execute the method
    var result = await _controller.GetUserById(testUser.Id);

    // Assert - Verify the outcome
    var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
    var user = okResult.Value.Should().BeOfType<User>().Subject;
    user.Name.Should().Be("Test");
}
```

### FluentAssertions
Tests use FluentAssertions for readable, expressive assertions:
```csharp
result.Should().BeOfType<OkObjectResult>();
users.Should().HaveCount(3);
user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
```

## ?? Test Utilities

### IDisposable Pattern
Test classes implement `IDisposable` to properly clean up resources:
```csharp
public void Dispose()
{
    _context.Database.EnsureDeleted();
    _context.Dispose();
}
```

## ?? Adding New Tests

1. Create a new test class in the appropriate folder
2. Inherit from appropriate base class or implement `IDisposable`
3. Follow the AAA pattern
4. Use FluentAssertions for assertions
5. Ensure test isolation (unique database per test)

### Example:
```csharp
public class NewControllerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly NewController _controller;

    public NewControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _controller = new NewController(_context);
    }

    [Fact]
    public async Task TestMethod_Scenario_ExpectedBehavior()
    {
        // Arrange
        // Act
        // Assert
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
```

## ?? Best Practices

1. **One Assert Per Test** - Each test should verify one specific behavior
2. **Clear Test Names** - Use pattern: `MethodName_Scenario_ExpectedBehavior`
3. **Test Isolation** - Each test should be independent
4. **Clean Up Resources** - Use IDisposable to clean up
5. **Readable Assertions** - Use FluentAssertions for clarity
6. **Arrange-Act-Assert** - Keep tests structured and readable

## ?? Debugging Tests

### Visual Studio
- Right-click on test ? Debug Test
- Set breakpoints in test code or controller code

### VS Code
- Use .NET Test Explorer extension
- Set breakpoints and run tests in debug mode

### Command Line
```bash
# Run tests with logger
dotnet test --logger "console;verbosity=detailed"
```

## ?? Continuous Integration

These tests are designed to run in CI/CD pipelines:
```yaml
# Example GitHub Actions
- name: Run tests
  run: dotnet test --no-build --verbosity normal
```

## ?? Future Enhancements

- [ ] Add integration tests
- [ ] Add code coverage reporting
- [ ] Add performance tests
- [ ] Add API contract tests
- [ ] Mock external dependencies with Moq
- [ ] Add test data builders/factories
