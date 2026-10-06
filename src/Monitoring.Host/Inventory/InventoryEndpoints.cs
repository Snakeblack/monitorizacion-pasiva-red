using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Monitoring.Domain.Inventory;
using Monitoring.Domain.Security;
using Monitoring.Host.Security;
using Monitoring.Persistence.Inventory;

namespace Monitoring.Host.Inventory;

// HTTP surface of the inventory. Authentication, role and scope checks happen first and are audited when refused; bodies are strict
// (known fields only, all required, bounded) and the rules themselves live in InventoryService, shared with any worker.
public static class InventoryEndpoints
{
    private const int MaximumBodyBytes = 16 * 1024;
    private const int DefaultPageSize = 50;

    private static readonly JsonSerializerOptions StrictBody = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    private sealed record CreateDevice(string Name, string Description, string SiteId, string SensorId);
    private sealed record UpdateDevice(long ExpectedRevision, string Name, string Description);
    private sealed record ConfirmCandidate(long ExpectedRevision, string DeviceName, string DeviceDescription);
    private sealed record RejectCandidate(long ExpectedRevision, string Reason);
    private sealed record MergeCandidate(long ExpectedRevision, Guid DeviceId, long ExpectedDeviceRevision);

    public static void MapInventoryEndpoints(this WebApplication app)
    {
        var inventory = app.MapGroup("/api/v1/inventory");
        inventory.MapGet("/devices", (HttpContext http, AccessGate gate, [FromServices] InventoryService? service, InventoryCursor cursors, TimeProvider time) =>
            ListAsync(http, gate, service, cursors, time, Operation.ReadInventory, "devices", allowState: false,
                (actor, state, size, after, token) => service!.ListDevicesAsync(actor.Scopes, size, after, token)));
        inventory.MapGet("/candidates", (HttpContext http, AccessGate gate, [FromServices] InventoryService? service, InventoryCursor cursors, TimeProvider time) =>
            ListAsync(http, gate, service, cursors, time, Operation.ReadCandidatesAndObservations, "candidates", allowState: true,
                (actor, state, size, after, token) => service!.ListCandidatesAsync(actor.Scopes, state, size, after, token)));
        inventory.MapGet("/observations", (HttpContext http, AccessGate gate, [FromServices] InventoryService? service, InventoryCursor cursors, TimeProvider time) =>
            ListAsync(http, gate, service, cursors, time, Operation.ReadCandidatesAndObservations, "observations", allowState: false,
                (actor, state, size, after, token) => service!.ListObservationsAsync(actor.Scopes, size, after, token)));

        inventory.MapPost("/devices", (HttpContext http, AccessGate gate, [FromServices] InventoryService? service) =>
            DecideAsync<CreateDevice, DeviceView>(http, gate, service, null, (actor, body, token) =>
                service!.CreateDeviceAsync(actor, body.Name, body.Description, new(body.SiteId, body.SensorId), token), created: true));
        inventory.MapPatch("/devices/{id}", (string id, HttpContext http, AccessGate gate, [FromServices] InventoryService? service) =>
            DecideAsync<UpdateDevice, DeviceView>(http, gate, service, id, (actor, body, token) =>
                service!.UpdateDeviceAsync(actor, Guid.Parse(id), body.ExpectedRevision, body.Name, body.Description, token)));
        inventory.MapPost("/candidates/{id}/confirm", (string id, HttpContext http, AccessGate gate, [FromServices] InventoryService? service) =>
            DecideAsync<ConfirmCandidate, CandidateView>(http, gate, service, id, (actor, body, token) =>
                service!.ConfirmCandidateAsync(actor, Guid.Parse(id), body.ExpectedRevision, body.DeviceName, body.DeviceDescription, token)));
        inventory.MapPost("/candidates/{id}/reject", (string id, HttpContext http, AccessGate gate, [FromServices] InventoryService? service) =>
            DecideAsync<RejectCandidate, CandidateView>(http, gate, service, id, (actor, body, token) =>
                service!.RejectCandidateAsync(actor, Guid.Parse(id), body.ExpectedRevision, body.Reason, token)));
        inventory.MapPost("/candidates/{id}/merge", (string id, HttpContext http, AccessGate gate, [FromServices] InventoryService? service) =>
            DecideAsync<MergeCandidate, CandidateView>(http, gate, service, id, (actor, body, token) =>
                service!.MergeCandidateAsync(actor, Guid.Parse(id), body.ExpectedRevision, body.DeviceId, body.ExpectedDeviceRevision, token)));
    }

    private static async Task<IResult> ListAsync<T>(HttpContext http, AccessGate gate, InventoryService? service, InventoryCursor cursors, TimeProvider time,
        Operation operation, string listing, bool allowState, Func<InventoryActor, string?, int, string?, CancellationToken, Task<InventoryPage<T>>> read)
    {
        var access = await gate.AuthorizeAsync(http, operation);
        if (access.Outcome == AccessOutcome.Unauthenticated) return Results.Unauthorized();
        if (access.Outcome == AccessOutcome.Forbidden) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (service is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (!TryReadQuery(http.Request.Query, allowState, out var pageSize, out var state, out var cursor)) return Results.StatusCode(StatusCodes.Status400BadRequest);

        var scopes = access.Scopes!;
        string? after = null;
        var query = $"{listing}|{state}";
        var now = time.GetUtcNow();
        if (cursor is not null && !cursors.TryOpen(cursor, access.Subject!, scopes, query, pageSize, now, out after, out var failure))
            return Results.StatusCode(failure switch
            {
                InventoryCursorFailure.Forbidden => StatusCodes.Status403Forbidden,
                InventoryCursorFailure.Expired => StatusCodes.Status410Gone,
                _ => StatusCodes.Status400BadRequest
            });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var actor = new InventoryActor(access.Subject!, scopes);
            var page = await read(actor, state, pageSize, after, timeout.Token);
            return Results.Ok(new { items = page.Items, nextCursor = page.Next is null ? null : cursors.Issue(access.Subject!, scopes, query, pageSize, page.Next, now) });
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (ArgumentException)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> DecideAsync<TBody, TValue>(HttpContext http, AccessGate gate, InventoryService? service, string? id,
        Func<InventoryActor, TBody, CancellationToken, Task<InventoryResult<TValue>>> decide, bool created = false) where TBody : class
    {
        var access = await gate.AuthorizeAsync(http, Operation.ManageInventory);
        if (access.Outcome == AccessOutcome.Unauthenticated) return Results.Unauthorized();
        if (access.Outcome == AccessOutcome.Forbidden) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (service is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (id is not null && !Guid.TryParseExact(id, "D", out _)) return Results.StatusCode(StatusCodes.Status400BadRequest);

        if (http.Request.ContentLength > MaximumBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            var block = new byte[4096];
            int read;
            while ((read = await http.Request.Body.ReadAsync(block, http.RequestAborted)) > 0)
            {
                buffer.Write(block, 0, read);
                if (buffer.Length > MaximumBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            bytes = buffer.ToArray();
        }
        TBody? body;
        try { body = JsonSerializer.Deserialize<TBody>(bytes, StrictBody); }
        catch (JsonException) { return Results.StatusCode(StatusCodes.Status400BadRequest); }
        if (body is null) return Results.StatusCode(StatusCodes.Status400BadRequest);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        InventoryResult<TValue> result;
        try { result = await decide(new InventoryActor(access.Subject!, access.Scopes!), body, timeout.Token); }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested) { return Results.StatusCode(StatusCodes.Status504GatewayTimeout); }
        if (result.Status == InventoryStatus.Forbidden)
            await gate.DenyAsync(http, access, Operation.ManageInventory, result.Code ?? "scope-not-authorized");
        return result.Status switch
        {
            InventoryStatus.Ok => created ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : Results.Ok(result.Value),
            InventoryStatus.NotFound => Results.Json(new { code = result.Code }, statusCode: StatusCodes.Status404NotFound),
            InventoryStatus.Conflict => Results.Json(new { code = result.Code }, statusCode: StatusCodes.Status409Conflict),
            InventoryStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => Results.Json(new { code = result.Code }, statusCode: StatusCodes.Status400BadRequest)
        };
    }

    // Known parameters only, each at most once, non-empty; pageSize 1..100 (default 50); state only where the listing has one.
    private static bool TryReadQuery(IQueryCollection query, bool allowState, out int pageSize, out string? state, out string? cursor)
    {
        pageSize = DefaultPageSize;
        state = null;
        cursor = null;
        foreach (var (name, values) in query)
        {
            if (values.Count != 1 || string.IsNullOrEmpty(values[0])) return false;
            switch (name)
            {
                case "pageSize":
                    if (!int.TryParse(values[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out pageSize)
                        || pageSize is < 1 or > InventoryLimits.MaximumPageSize) return false;
                    break;
                case "cursor" when values[0]!.Length <= 4096:
                    cursor = values[0];
                    break;
                case "state" when allowState && values[0] is "candidate" or "confirmed" or "rejected":
                    state = values[0];
                    break;
                default:
                    return false;
            }
        }
        return true;
    }
}
