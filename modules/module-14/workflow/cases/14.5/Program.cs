var inputs = new[] { "Allow", "Block", "Unavailable" };

foreach (var value in inputs)
    Console.WriteLine($"{value}: {Route(value)}");

string Route(string value) => value switch
{
    "Allow" => "Continue",
    "Block" => "Support",
    _ => Fallback(),
};

string Fallback() =>
    "Human review";
