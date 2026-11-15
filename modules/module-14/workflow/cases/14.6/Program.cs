var inputs = new[] { "What is my delivery status?",
    "Any news on my parcel?", "Can I reset my password?" };

foreach (var value in inputs)
    Console.WriteLine($"{value} => {(Permit(value) ? "Delivery status" : "Review")}");

bool Permit(string value) =>
    value.Contains("delivery status", StringComparison.OrdinalIgnoreCase)
    || value.Contains("news on my parcel", StringComparison.OrdinalIgnoreCase);
