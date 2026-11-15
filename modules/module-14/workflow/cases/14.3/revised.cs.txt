var inputs = new[] { "north/customer", "north/staff", "south/customer" };

foreach (var value in inputs)
    Console.WriteLine($"{value}: {(Permit(value) ? "Include" : "Exclude")}");

bool Permit(string value) =>
    value == "north/customer";
