var inputs = new[] { "LookupOrder", "Unsupported" };

foreach (var value in inputs)
    Console.WriteLine($"{value}: {(Permit(value) ? "Allow" : "Deny")}");

bool Permit(string value) =>
    value == "LookupOrder";
