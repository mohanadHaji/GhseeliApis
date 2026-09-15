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

Console.Error.WriteLine(
    "Usage:\n" +
    "  dotnet run --project Ghseeli.DemoData -- export [output-path]\n" +
    "  dotnet run --project Ghseeli.DemoData -- seed <customer-connection> <business-connection>");
Environment.ExitCode = 2;
