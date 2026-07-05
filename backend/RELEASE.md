# Release runbook

Walk this list before every release to production. The automated suite
([TESTING.md](TESTING.md)) catches what it can; this is for everything else.

## 1. Pre-release: automated gates (must be green)

```powershell
# Local quick-pass — should finish in ~60 s
dotnet test RentCollection.Tests/RentCollection.Tests.csproj
dotnet test RentCollection.IntegrationTests/RentCollection.IntegrationTests.csproj `
  --filter "FullyQualifiedName!~SqlServerFixture&FullyQualifiedName!~AppVersionEndpointTests.GetVersion_ReturnsConfiguredMinSupportedVersion&FullyQualifiedName!~InvoicePdfEndpointTests"

# E2E — boot backend first, then in another terminal:
dotnet test RentCollection.E2ETests/RentCollection.E2ETests.csproj
```

If any are red, **stop and investigate** — the release waits.

## 2. Manual QA — staging (K-NET sandbox env)

Run these against `https://stage.rent.alrtam.com` (or whatever your staging
URL is) with the K-NET **sandbox** terminal credentials configured. K-NET
publishes test cards in their integration docs — keep one handy.

### Payment happy path
```
☐ Open tenant portal, sign in as a real tenant (not the E2E fixture row)
☐ Open Make Payment, select an unpaid invoice
☐ Confirm the displayed amount matches the invoice TotalAmount - PaidAmount
☐ Click Pay with K-NET → redirected to KPG sandbox page
☐ Enter K-NET sandbox CAPTURED test card → submit
☐ Land on /TenantPortal/PaymentSuccess with a reference number
☐ Verify invoice now shows Paid in the portal
☐ Verify Payment row in /Payments admin view: Status=Completed, ReceiptNumber populated, AmountPaid matches
☐ Verify Invoice row in /Invoices admin view: Status=Paid, PaidAmount=TotalAmount
```

### Payment declined path
```
☐ Open Make Payment, pick another unpaid invoice
☐ Pay with K-NET → enter sandbox DENIED test card → submit
☐ Land on /TenantPortal/PaymentFailed with an error message
☐ Invoice remains Pending
☐ Payment row exists with Status=Failed, no ReceiptNumber
```

### Browser-close mid-payment
```
☐ Open Make Payment, click Pay with K-NET
☐ When redirected to the KPG sandbox page, CLOSE the browser tab without confirming
☐ Wait 2 minutes
☐ Check the admin Payments view: the row should still be Pending OR (after
  the Hangfire 'reconcile' cycle) marked Cancelled
☐ The invoice must NOT be marked Paid
```

### Partial payment composition
```
☐ Pick an invoice with TotalAmount = 500 KWD
☐ Submit a 200 KWD bank transfer (admin approves it in /PaymentApproval)
☐ Invoice should be Partially Paid with balance 300 KWD
☐ Tenant pays the remaining 300 KWD via K-NET sandbox CAPTURED
☐ Invoice flips to Paid; total Payment rows = 2 (bank + K-NET)
☐ Total received reconciles to TotalAmount
```

### Replay defense
```
☐ Complete a K-NET sandbox capture as above
☐ From the browser dev tools, copy the POST body that hit /api/payments/knet/response
☐ Replay it via curl/PowerShell against the same endpoint
☐ Expected: response redirects to PaymentFailed OR returns the cached
  success — but a SECOND Payment row must NOT appear in the admin Payments view
☐ Invoice PaidAmount stays at the original amount, not doubled
```

### IDOR check
```
☐ Sign in as tenant A in one browser
☐ Sign in as tenant B in an incognito window
☐ Note the InvoiceId of an unpaid invoice belonging to tenant B
☐ From tenant A's session, attempt to initiate payment against that InvoiceId:
  curl -X POST -d '{"TenantId":<A>,"InvoiceId":<B's invoice>,"Amount":1.000}' \
    -H "Content-Type: application/json" \
    -b "TenantSession=<A's cookie>" \
    https://stage.../api/payments/knet/initiate
☐ Expected: 4xx with an "ownership" or "not found" error
☐ The Payments table must not gain a row for the cross-tenant attempt
```

### Kill switch
```
☐ In staging appsettings, set Knet:Enabled = false. Restart app.
☐ Tenant opens Make Payment → clicks Pay with K-NET
☐ Expected: 503 response with the "K-NET payments are disabled" body
☐ Open /api/payments/knet/response via curl with valid trackid+CAPTURED
☐ Expected: STILL processes (kill switch only gates initiate, not callbacks
  — in-flight payments must complete)
☐ Reset Knet:Enabled = true. Restart.
```

### Refund
```
☐ Sign in as admin, open the Payments view
☐ Pick the K-NET sandbox payment from the happy-path test
☐ Click Refund (full amount), confirm
☐ Verify a negative-amount Payment row appears
☐ Invoice status reverts to Pending (or Partially Paid if there's still other money)
☐ Tenant sees the refund in their portal
```

## 3. Pre-deploy: dashboards green

```
☐ /health/ready returns Healthy with database + hangfire green
☐ /JobHealth shows zero failure-streaks across all 7 recurring jobs
☐ /hangfire dashboard shows the scheduler heartbeating < 30 s ago
☐ smoke.ps1 against staging exits 0
```

## 4. Deploy

```
☐ Tag the release: git tag vYYYY.MM.DD && git push --tags
☐ Trigger deploy via your CI/CD
☐ Watch the deploy log — health checks must turn green before traffic is sent
☐ scripts/smoke.ps1 -BaseUrl https://rent.alrtam.com → exit 0
```

## 5. Post-deploy: 5-minute watch

```
☐ Open production /JobHealth — all scheduler servers heartbeating
☐ Open /hangfire — no Failed jobs in the last 5 minutes
☐ Make ONE small test payment from a real tenant account (refund it after)
☐ Verify the K-NET payment lands in the bank account within the expected window
☐ If anything looks wrong: flip Knet:Enabled=false in production config,
   restart, investigate. The kill switch is your friend.
```

## 6. Quarterly drill (independent of release)

```
☐ Run scripts/restore-drill.ps1 -BackupPath <latest backup>
  → exit 0 = backup integrity confirmed
☐ Walk through the payment-happy-path + payment-declined paths above
   on staging with a NEW K-NET sandbox card (rotates the test data)
☐ Review the K-NET MID + Tranportal credentials in appsettings — rotate if
   they haven't been rotated in >12 months
```

## Emergency response — payment gateway incident

**If the K-NET dashboard shows captures we didn't receive:**
1. `Knet:Enabled = false` in prod. Restart. Stops new charges immediately.
2. Pull the last hour of `/hangfire` logs + Payments table.
3. Run [`rcops compare-snapshot --period <yyyy-MM>`](RentCollection.OpsCli) to
   see if the divergence is in the aggregator or upstream.
4. Open the [`ReconciliationMatcher`](RentCollection.Application/Services/ReconciliationMatcher.cs)-backed
   admin view (`/Admin/KnetReconciliation` once shipped) to find orphaned
   bank lines.
5. Manually reconcile by creating Payment rows with `IsBackfilled=true` and
   note the incident ID in the Notes field.

**If a tenant claims they paid but the invoice is unpaid:**
1. Get their `paymentid` or `trackid` (their browser may still have it).
2. Look up Payments by `TransactionReference = <trackid>` — if it's there,
   K-NET captured but the callback didn't reach us.
3. Replay the callback by re-POSTing the recorded payload to
   `/api/payments/knet/response`. The idempotency guard makes this safe.
4. If no row exists at all, check K-NET's dashboard for the trackid. If
   they captured it, manually create a Payment row tagged with the
   trackid and refund the tenant — they paid for nothing if it's a phantom
   capture.
