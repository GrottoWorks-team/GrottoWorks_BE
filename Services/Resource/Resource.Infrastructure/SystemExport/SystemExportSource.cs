using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.SystemExport;

/// <summary>Schema version 1: explicit table/column allowlist. New columns are never exported implicitly.</summary>
public sealed class SystemExportSource(ResourceDbContext db)
{
    public static IReadOnlyDictionary<string, string[]> Columns { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>
    {
        ["borrow_transaction"] = ["borrow_transaction_id", "borrowed_item_id", "borrow_date", "return_date", "return_condition", "returned_quantity", "recorded_by_user_id", "transaction_note"],
        ["borrowed_item"] = ["borrowed_item_id", "offered_by_user_id", "accepted_by_user_id", "work_area_id", "task_id", "season_id", "owner_name", "owner_phone", "owner_address", "owner_note", "borrowed_item_name", "borrowed_item_quantity", "borrowed_item_description", "initial_condition", "expected_return_date", "borrowed_item_status", "accepted_at", "created_at", "rejection_reason", "last_reminded_on"],
        ["donation"] = ["donation_id", "donor_id", "season_id", "work_area_id", "donation_type", "donation_status", "pledged_amount", "amount", "donation_date", "created_by_user_id", "created_at", "updated_at", "cancel_reason", "donation_note"],
        ["donation_item"] = ["donation_item_id", "donation_id", "material_id", "pledged_quantity", "received_quantity", "unusable_quantity", "item_note"],
        ["donation_receipt"] = ["donation_receipt_id", "donation_id", "received_at", "received_by_user_id", "received_amount", "receipt_note", "created_at"],
        ["donation_receipt_item"] = ["donation_receipt_item_id", "donation_receipt_id", "donation_item_id", "received_quantity", "unusable_quantity", "item_note"],
        ["donor"] = ["donor_id", "parish_id", "user_id", "donor_name", "phone", "email", "address", "donor_note", "created_by_user_id", "created_at", "updated_at"],
        ["material"] = ["material_id", "material_category_id", "material_code", "material_name", "material_unit", "material_description", "material_status"],
        ["material_allocation"] = ["material_allocation_id", "work_area_id", "task_id", "allocated_by_user_id", "allocation_date", "allocation_note"],
        ["material_allocation_item"] = ["material_allocation_item_id", "material_allocation_id", "material_id", "allocated_quantity"],
        ["material_category"] = ["material_category_id", "category_code", "category_name", "category_description", "category_status"],
        ["material_requirement"] = ["material_requirement_id", "work_area_id", "material_id", "required_quantity", "existing_quantity", "requirement_status", "requirement_note", "created_at", "updated_at"],
        ["material_transaction"] = ["material_transaction_id", "material_id", "work_area_id", "transaction_type", "transaction_quantity", "transaction_date", "reference_type", "reference_id", "created_by_user_id", "transaction_note"],
        ["purchase"] = ["purchase_id", "purchase_request_id", "task_id", "buyer_user_id", "confirmed_by_user_id", "supplier_name", "purchase_date", "total_cost", "purchase_status", "confirmed_at", "cost_overrun_approved_by_user_id", "cost_overrun_approved_at", "receipt_file_ids", "info_request_reason", "created_at"],
        ["purchase_item"] = ["purchase_item_id", "purchase_id", "purchase_request_item_id", "material_id", "purchased_quantity", "unit_price", "total_price", "received_usable_quantity", "received_unusable_quantity"],
        ["purchase_request"] = ["purchase_request_id", "work_area_id", "requested_by_user_id", "approved_by_user_id", "request_reason", "request_status", "decision_note", "approved_at", "rejected_at", "created_at", "updated_at"],
        ["purchase_request_item"] = ["purchase_request_item_id", "purchase_request_id", "material_id", "requested_quantity", "estimated_cost", "item_note", "shortage_override_reason"],
        ["purchase_request_state_change"] = ["purchase_request_state_change_id", "purchase_request_id", "from_status", "to_status", "changed_by_user_id", "changed_at", "change_note"],
        ["support_transfer"] = ["support_response_material_id", "source_work_area_id", "target_work_area_id", "material_id", "transfer_quantity", "transfer_status", "approved_by_user_id", "transferred_at", "received_usable_quantity", "received_unusable_quantity", "received_confirmed_by_user_id", "received_confirmed_at", "transfer_note"],
        ["support_transfer_operation"] = ["operation_id", "support_response_material_id", "source_work_area_id", "target_work_area_id", "material_id", "quantity", "approved_by_user_id", "status", "error_code", "decided_at"],
        ["task_material_plan"] = ["task_id", "work_area_id", "parish_id", "season_id", "source_version", "updated_at"],
        ["task_material_plan_item"] = ["task_id", "material_id", "required_quantity", "material_note"],
        ["task_material_report"] = ["task_material_report_id", "task_id", "reported_by", "verified_by", "report_status", "report_note", "revision_reason", "reported_at", "verified_at"],
        ["task_material_report_item"] = ["task_material_report_item_id", "task_material_report_id", "material_allocation_item_id", "reported_remaining_quantity", "reported_damaged_quantity", "reported_lost_quantity", "verified_remaining_quantity", "verified_damaged_quantity", "verified_lost_quantity", "item_note"],
    });

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        using var writer = new StreamWriter(destination, new UTF8Encoding(false), 65536, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "begin", service = "Resource", schemaVersion = 1, startedAt }).AsMemory(), cancellationToken);
        var counts = new Dictionary<string, long>();
        foreach (var (table, columns) in Columns)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandTimeout = 600;
            // Identifiers come exclusively from the source-code allowlist above.
            var projection = string.Join(", ", columns.Select(column => $"\"{column}\""));
            command.CommandText = $"SELECT row_to_json(export_row)::text FROM (SELECT {projection} FROM \"{table}\") export_row";
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            long count = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = reader.GetString(0);
                await writer.WriteLineAsync(($"{{\"type\":\"row\",\"table\":\"{table}\",\"data\":{row}}}").AsMemory(), cancellationToken);
                count++;
            }
            counts.Add(table, count);
        }
        await transaction.CommitAsync(cancellationToken);
        // A consumer MUST reject a response without this footer, even if HTTP already returned 200.
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "end", service = "Resource", schemaVersion = 1, complete = true, startedAt, finishedAt = DateTimeOffset.UtcNow, counts }).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
