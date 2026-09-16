using Ghseeli.DemoData;

if (args.Length == 0 || args[0].Equals("export", StringComparison.OrdinalIgnoreCase))
{
    var outputPath = args.Length >= 2
        ? args[1]
        : Path.Combine("demo-data", "frontend-demo-data.json");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    await File.WriteAllTextAsync(outputPath, DemoDataJson.Serialize(DemoDataDefinition.Create()));
    Console.WriteLine($"Wrote clearly labeled frontend demo data to {Path.GetFullPath(outputPath)}");
    return;
}

if (args[0].Equals("seed", StringComparison.OrdinalIgnoreCase) && args.Length == 3)
{
    var result = await DemoDatabaseSeeder.SeedAsync(args[1], args[2]);
    Console.WriteLine(
        result.AlreadySeeded
            ? "Demo databases already contained the complete deterministic dataset; no duplicates were added."
            : $"Seeded {result.CompanyCount} companies, {result.CustomerCount} customers, " +
              $"{result.CustomerBookingCount} customer bookings, and {result.BusinessReservationCount} business reservations.");
    return;
}

if (args[0].Equals("seed-hosted", StringComparison.OrdinalIgnoreCase) && args.Length == 3)
{
    var result = await DemoDatabaseSeeder.SeedHostedAsync(
        args[1],
        args[2],
        Environment.GetEnvironmentVariable("GITHUB_REPOSITORY"),
        Environment.GetEnvironmentVariable("GITHUB_REF"),
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
        Environment.GetEnvironmentVariable("GHSEELI_HOSTED_DEMO_CONFIRMATION"));
    Console.WriteLine(
        result.AlreadySeeded
            ? "Hosted databases already contained the complete deterministic Demo dataset; no duplicates were added."
            : $"Seeded {result.CompanyCount} hosted Demo companies, {result.CustomerCount} Demo customers, " +
              $"{result.CustomerBookingCount} Customer bookings, and {result.BusinessReservationCount} Business reservations.");
    return;
}

if (args[0].Equals("cleanup", StringComparison.OrdinalIgnoreCase) && args.Length == 3)
{
    var result = await DemoDatabaseSeeder.CleanupAsync(args[1], args[2]);
    Console.WriteLine(
        $"Deleted {result.CompanyCount} demo companies, {result.CustomerCount} demo customers, " +
        $"{result.CustomerBookingCount} customer bookings, and " +
        $"{result.BusinessReservationCount} business reservations.");
    return;
}

Console.Error.WriteLine(
    "Usage:\n" +
    "  dotnet run --project Ghseeli.DemoData -- export [output-path]\n" +
    "  dotnet run --project Ghseeli.DemoData -- seed <customer-connection> <business-connection>\n" +
    "  dotnet run --project Ghseeli.DemoData -- seed-hosted <customer-connection> <business-connection>\n" +
    "  dotnet run --project Ghseeli.DemoData -- cleanup <customer-connection> <business-connection>");
Environment.ExitCode = 2;
