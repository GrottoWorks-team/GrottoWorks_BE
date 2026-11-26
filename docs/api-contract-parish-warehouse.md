# API contract — Parish warehouse + hand-downs (F-RES-21)

Status: implemented in Resource (2026-11-26). Gateway routes: `parish-inventory`, `parish-inventory-adjustments`,
`parish-distributions` (cluster `resource`, policy `authenticated`).

## Roles

| Action | Role |
|---|---|
| Opening stock, adjustment, issue, cancel | `PARISH` |
| Read balances/history (and ADMIN with `parishId`) | `PARISH`, `ADMIN` |
| Confirm receipt | `MO` of the target work area |
| List hand-downs | `PARISH`, `LEADER`, `MO`, `ADMIN` (scoped) |

`MO` never reads or writes the parish ledger; it lives in its own table.

## Endpoints

### POST /api/v1/parish-inventory/opening-stock

```json
{ "seasonId": "uuid", "materialId": "uuid", "quantity": 10, "transactionDate": "2026-11-20T08:00:00+07:00", "note": "Kiểm kê" }
```

- 201: `{ "data": { "id", "materialId", "transactionType": "OPENING_STOCK", "quantity", "transactionDate", ... } }`
- 409 `PARISH_OPENING_STOCK_ALREADY_RECORDED` — one opening stock per (parish, season, material).

### GET /api/v1/parish-inventory?seasonId=&materialId=&page=&size=&parishId=

- `parishId` only honoured for `ADMIN`.
- 200 paged: `{ "data": [{ "materialId", "materialCode", "materialName", "unit", "confirmedAvailable" }], "page": { ... } }`.

### GET /api/v1/parish-inventory/history?seasonId=&materialId=&from=&to=&parishId=

- 200 `{ "data": [{ "id", "materialId", "transactionType", "quantity", "transactionDate", "referenceType", "referenceId", "note" }] }`
- At most 500 rows, newest first.

### POST /api/v1/parish-inventory-adjustments

```json
{ "seasonId": "uuid", "materialId": "uuid", "type": "DAMAGED", "quantity": 2, "reason": "Vỡ khi vận chuyển" }
```

- 409 `PARISH_STOCK_INSUFFICIENT` when the quantity exceeds the current balance.

### POST /api/v1/parish-distributions

```json
{ "seasonId": "uuid", "targetWorkAreaId": "uuid", "items": [{ "materialId": "uuid", "quantity": 10 }], "note": "..." }
```

- 201 `{ "data": ParishDistribution }`, status `IN_TRANSIT`; posts `DISTRIBUTION_OUT` in the parish ledger.
- 409 `PARISH_STOCK_INSUFFICIENT`; 404 `WORK_AREA_NOT_FOUND` (other parish/season).

### GET /api/v1/parish-distributions?seasonId=&workAreaId=&status=&page=&size=

```json
{
  "id": "uuid", "parishId": "uuid", "seasonId": "uuid", "targetWorkAreaId": "uuid", "targetWorkAreaName": "...",
  "status": "IN_TRANSIT", "origin": "MANUAL", "issuedBy": "uuid", "issuedAt": "...",
  "receivedConfirmedBy": null, "receivedConfirmedAt": null, "note": "...", "cancelReason": null,
  "items": [{ "id": "uuid", "materialId": "uuid", "materialCode", "materialName", "unit", "issuedQuantity", "receivedUsableQuantity", "receivedUnusableQuantity" }],
  "version": 1
}
```

### GET /api/v1/parish-distributions/{id}

Same shape; `PARISH` in the same parish, `LEADER`/`MO` only for their assigned work area.

### POST /api/v1/parish-distributions/{id}/confirm-receipt

```json
{ "version": 1, "items": [{ "itemId": "uuid", "usableQuantity": 9, "unusableQuantity": 1 }], "note": "..." }
```

- `usable + unusable <= issued`; only `usable` posts `TRANSFER_IN` in the work-area ledger (reference
  `PARISH_DISTRIBUTION_ITEM` = item id).
- 409 `PARISH_DISTRIBUTION_INVALID_STATE`, 412 `PARISH_DISTRIBUTION_VERSION_MISMATCH`,
  404 `PARISH_DISTRIBUTION_NOT_FOUND` (outside the officer's scope).

### POST /api/v1/parish-distributions/{id}/cancel

```json
{ "version": 1, "reason": "Khu không cần nữa" }
```

- Only `IN_TRANSIT`; posts `DISTRIBUTION_RETURN` restoring the parish balance.

### GET /api/v1/parish-donors?q=&page=&size=

Donor picker for a material donation into the parish warehouse (the material-officer `GET /api/v1/donors` is not
served to `PARISH`). Paged `DonorDto[]` of the caller's parish only. Creating/updating donors stays with the material
officer (`POST|PATCH /api/v1/donors`), so the parish committee picks from existing donors.

## Purchase flow changes

| Endpoint | Change |
|---|---|
| `POST /purchase-requests/{id}/approve|reject` | Role `PARISH` (was `LEADER`) |
| `POST /purchases/{id}/approve-cost-overrun` | Role `PARISH` |
| `POST /purchase-requests/{id}/purchases` | Role `MO` of the request's work area; buyer = `PARISH` assignee of the PURCHASE task, else the `PARISH` user who approved the request; `recordedBy` = MO |
| `POST /purchases/{id}/confirm-delivery` | Role `MO`; posts `PURCHASE_DELIVERY` to the parish ledger and auto-creates an `IN_TRANSIT` distribution (`origin = PURCHASE`) for the requesting area |

New error codes: `PURCHASE_TASK_REQUIRED` (409), `PURCHASE_BUYER_NOT_ASSIGNED` (409), `PURCHASE_BUYER_NOT_PARISH` (422).

## Donations

`POST /api/v1/donations` now allows `PARISH` in addition to `MO`:

- `MATERIAL` + `workAreaId: null` → parish warehouse (only `PARISH`);
- `MATERIAL` + `workAreaId` → work-area stock (MO of that area);
- `MONEY` unchanged.

## Events

| Event | Payload |
|---|---|
| `parish.material.stock.changed` | parishId, seasonId, materialId, confirmedAvailable, occurredAt, code/name/unit |
| `parish.distribution.issued` | distributionId, parishId, seasonId, targetWorkAreaId, targetMaterialOfficerUserId, lineCount, issuedByUserId, issuedAt |
| `parish.distribution.received` | distributionId, parishId, seasonId, targetWorkAreaId, confirmedByUserId, issuedByUserId, receivedUsableQuantity, receivedUnusableQuantity, receivedAt |
