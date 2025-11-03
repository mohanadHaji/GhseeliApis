# ?? Validation Quick Reference

## Interface Implementation

```csharp
// 1. Implement IValidatable interface
public class YourModel : IValidatable
{
    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };
        
        // Add validation rules
        if (string.IsNullOrWhiteSpace(Field))
            result.AddError("Field is required.");
            
        return result;
    }
}
```

## Controller Usage

```csharp
// 2. Call Validate() in controller
[HttpPost]
public async Task<IActionResult> Create([FromBody] YourModel model)
{
    var validation = model.Validate();
    if (!validation.IsValid)
    {
        return BadRequest(new
        {
            Message = "Validation failed",
            Errors = validation.Errors
        });
    }
    
    // Save to database...
}
```

## Current Validation Rules

### User Model
- **Name**: Required, 2-100 characters
- **Email**: Required, valid format, max 200 characters

## API Error Response

```json
{
  "message": "Validation failed",
  "errors": [
    "Name is required.",
    "Email format is invalid."
  ]
}
```

## Running Tests

```bash
# All tests (44 tests)
dotnet test

# Only validation tests (25 tests)
dotnet test --filter "ValidationTests"
```

## Test Count
- **Total**: 44 tests
- **Validation**: 25 tests
- **Pass Rate**: 100% ?
