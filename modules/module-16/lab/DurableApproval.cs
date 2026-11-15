using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;

sealed class DurableApproval(TableClient table, TimeProvider clock)
{
    private const string Callback = "Request callback";
    internal static string Partition(CustomerScope scope) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope.Tenant+"/"+scope.Customer)));

    internal async Task<Reply> OrderAsync(CustomerScope scope,string? id,CancellationToken ct)
    {
        var order = await GetOrderAsync(scope,id,ct);
        return order is null ? new("missing") : new("found",order.GetString("Status"));
    }

    internal static bool ValidOrderId(string? id) => ValidId(id,"ORD-",4);

    internal Task<TableEntity?> GetOrderAsync(CustomerScope scope,string? id,CancellationToken ct) =>
        ValidOrderId(id) ? ReadAsync(Partition(scope),"order:"+id,ct) : Task.FromResult<TableEntity?>(null);

    internal async Task<Reply> ProposeAsync(CustomerScope scope,string? ticket,string? action,CancellationToken ct)
    {
        if (action != Callback) return new("unsupported_change");
        if (!ValidId(ticket,"T-",6)) return new("denied");
        var partition = Partition(scope);
        var target = await ReadAsync(partition,"ticket:"+ticket,ct);
        if (target is null) return new("denied");
        if (target.GetBoolean("CallbackRequested")==true) return new("already_applied",Callback);
        var id = Guid.NewGuid().ToString("N");
        await HostedTelemetry.DependencyAsync("Azure Table add proposal", () => table.AddEntityAsync(new TableEntity(partition,"proposal:"+id)
        {
            ["Ticket"]=ticket!, ["Actor"]=scope.Actor, ["Action"]=Callback,
            ["Expires"]=clock.GetUtcNow().AddMinutes(15), ["Applied"]=false
        },ct));
        return new("approval_required",id);
    }

    internal async Task<Reply> ApproveAsync(CustomerScope scope,string? id,CancellationToken ct)
    {
        if (!Guid.TryParseExact(id,"N",out var key)) return new("denied");
        var partition = Partition(scope);
        for (var attempt=0; attempt<3; attempt++)
        {
            var proposal = await ReadAsync(partition,"proposal:"+key.ToString("N"),ct);
            if (proposal is null || proposal.GetString("Actor")!=scope.Actor || proposal.GetString("Action")!=Callback)
                return new("denied");
            if (proposal.GetBoolean("Applied")==true) return new("already_applied",Callback);
            if (proposal.GetDateTimeOffset("Expires") is not { } expires || expires <= clock.GetUtcNow())
                return new("approval_expired");
            var ticketId = proposal.GetString("Ticket");
            if (!ValidId(ticketId,"T-",6)) return new("denied");
            var ticket = await ReadAsync(partition,"ticket:"+ticketId,ct);
            if (ticket is null) return new("denied");
            if (ticket.GetBoolean("CallbackRequested")==true) return new("already_applied",Callback);
            proposal["Applied"]=true;
            ticket["CallbackRequested"]=true;
            try
            {
                await HostedTelemetry.DependencyAsync("Azure Table approval transaction", () => table.SubmitTransactionAsync([
                    new(TableTransactionActionType.UpdateReplace,ticket,ticket.ETag),
                    new(TableTransactionActionType.UpdateReplace,proposal,proposal.ETag)
                ],ct));
                return new("applied",Callback,Writes:1);
            }
            catch (RequestFailedException ex) when (ex.Status==412)
            {
                // Re-read the winning state. The transaction changes both rows or neither.
            }
        }
        return new("retry_later");
    }

    private async Task<TableEntity?> ReadAsync(string partition,string row,CancellationToken ct)
    {
        var result = await HostedTelemetry.DependencyAsync("Azure Table get entity", () =>
            table.GetEntityIfExistsAsync<TableEntity>(partition,row,cancellationToken:ct));
        return result.HasValue ? result.Value : null;
    }

    private static bool ValidId(string? value,string prefix,int maxDigits) =>
        value is not null && value.StartsWith(prefix,StringComparison.Ordinal) &&
        value.Length>prefix.Length && value.Length<=prefix.Length+maxDigits &&
        value.Skip(prefix.Length).All(char.IsAsciiDigit) &&
        (prefix!="ORD-" || value.Length==prefix.Length+maxDigits);
}
