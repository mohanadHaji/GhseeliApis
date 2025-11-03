# ?? Quick Test Reference

## Run All Tests
```bash
dotnet test
```

## Run Tests with Details
```bash
dotnet test --verbosity detailed
```

## Run Specific Test Class
```bash
# Users Controller Tests
dotnet test --filter "FullyQualifiedName~UsersControllerTests"

# Health Controller Tests
dotnet test --filter "FullyQualifiedName~HealthControllerTests"

# Hello Controller Tests
dotnet test --filter "FullyQualifiedName~HelloControllerTests"
```

## Run Specific Test Method
```bash
dotnet test --filter "FullyQualifiedName~GetAllUsers_ReturnsAllUsers_WhenUsersExist"
```

## Watch Mode (Re-run on file changes)
```bash
dotnet watch test --project GhseeliApis.Tests
```

## Generate Test Report
```bash
dotnet test --logger "html;logfilename=testResults.html"
```

## Test Count by Controller
- **UsersController**: 17 tests
- **HealthController**: 10 tests  
- **HelloController**: 5 tests
- **Total**: 28 tests ?

## All Tests Passing! ??
```
Test Run Successful.
Total tests: 28
     Passed: 28
 Total time: ~1.5s
```
