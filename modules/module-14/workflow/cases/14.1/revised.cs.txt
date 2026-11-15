var owner = "customer-a";
var inputs = new[] { "customer-a", "customer-b" };

foreach (var value in inputs)
    Console.WriteLine($"{value}: {(Permit(value) ? "Allow" : "Deny")}");

bool Permit(string value) =>
    value == owner;
