if (args.Length > 0 && args[0] != "14.1") return DefensiveChecks.Run(args[0]);

var customer = new Principal("north", "customer-a");
var ownOrder = new Order("A-104", "north", "customer-a");
var otherOrder = new Order("A-105", "north", "customer-b");

Console.WriteLine("ownership");
Console.WriteLine($"Principal: {customer.Tenant}/{customer.Customer}");
Console.WriteLine($"{ownOrder.Id} (own order)   => {Decide(customer, ownOrder)}");
Console.WriteLine($"{otherOrder.Id} (other owner) => {Decide(customer, otherOrder)}");
if (Decide(customer, ownOrder) != "Allowed" || Decide(customer, otherOrder) != "Denied")
    return 1;

Risk[] inventory = [
    new("Session", "Customer -> App", "Verify session", "Gateway"),
    new("Private policy", "Index -> Model", "Filter by document scope", "Retrieval"),
    new("Order record", "App -> Order tool", "Verify tenant and owner", "Orders"),
    new("Order details", "App -> Logs", "Project status fields only", "Telemetry")
];
Console.WriteLine("\ncoverage");
Console.WriteLine($"{"Asset",-17} {"Boundary",-19} {"Control",-28} Owner");
foreach (var risk in inventory)
    Console.WriteLine($"{risk.Asset,-17} {risk.Boundary,-19} {risk.Control,-28} {risk.Owner}");
return inventory.All(r => r.Control.Length > 0 && r.Owner.Length > 0) ? 0 : 1;

static string Decide(
    Principal principal, Order order) =>
    principal.Tenant == order.Tenant &&
    principal.Customer == order.Customer
        ? "Allowed"
        : "Denied";

record Principal(string Tenant, string Customer);
record Order(string Id, string Tenant, string Customer);
record Risk(string Asset, string Boundary, string Control, string Owner);

