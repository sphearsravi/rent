# RentCollection — Test Plan for Delivery

**Project:** ALRTAM Property Management Suite (VIIJAR — *Pay Rent Easy*)
**Customer:** ALRTAM Company, Kuwait
**Vendor:** Sphears (Ravi Shankar)
**Plan version:** 1.0 · 2026-06-09 · Sprint 23
**Plan owner:** Test Lead (TBA)
**Sign-off authority:** ALRTAM Operations Head + Vendor Project Manager

This document is the **delivery test plan** — the formal QA contract
between vendor and customer. It defines what will be tested, by whom,
to what standard, before the system is accepted into production.

Companion documents:
- [SYSTEM_WORKFLOW.md](SYSTEM_WORKFLOW.md) — what the system does (read first)
- [TESTER_GUIDE.md](TESTER_GUIDE.md) — walkthrough for manual testers
- [TEST_CASES.md](TEST_CASES.md) — the formal test case catalogue (~100 IDs)

---

## 1. Objectives

By the end of the testing phase the team must be able to confidently
say:

1. **The system performs every documented business workflow correctly.**
2. **Data integrity is uncompromised** across concurrent operations,
   role boundaries, and Kuwait-specific value formats (KWD precision,
   Arabic text, PACI checksums).
3. **Money never goes missing.** Every payment is accounted for,
   reconcilable, and auditable.
4. **The customer can run the system without daily vendor support.**
5. **No P0 (release-blocker) bugs are open.**
6. **A signed-off User Acceptance Test (UAT) exists.**

---

## 2. Scope

### 2.1 In scope

| Area                    | Coverage                                                     |
| ----------------------- | ------------------------------------------------------------ |
| **Authentication**      | Staff portal + Tenant portal + JWT mobile API                |
| **Property module**     | Create, Edit, List, Delete, Documents                        |
| **Unit module**         | Create, Edit, Status reconcile, deep-link to Lease           |
| **Tenant module**       | Create, Edit, Civil ID parser, Gemini scan, Documents        |
| **Lease module**        | Create, Activate, Terminate, Renew, lifecycle states         |
| **Invoicing**           | Daily rent / insurance / late-fee jobs, PDF generation       |
| **Payments**            | Cash, Bank Transfer (with approval), K-NET (sandbox)         |
| **Maintenance**         | Request → Assign → Complete, photos, cost capture            |
| **Documents**           | Polymorphic upload/link/download                             |
| **Reports**             | Excel + PDF exports across 6 standard reports                |
| **Tenant portal**       | Dashboard, invoices, payment, maintenance, documents          |
| **Background jobs**     | All Hangfire recurring + retry/idempotency                   |
| **Security**            | Role-based access, CompanyId isolation, XSS, SQL injection   |
| **Performance**         | Page-load thresholds, list pagination, PDF generation time   |
| **Localization**        | Arabic name handling, KWD 3-decimal precision, RTL where applicable |
| **Cross-browser**       | Chrome, Edge, Firefox, Safari (iOS), Chrome (Android)        |
| **K-NET sandbox**       | Happy path, cancel, duplicate guard, reconciliation          |
| **Deployment**          | Production deploy procedure verified end-to-end              |

### 2.2 Out of scope (this delivery)

- K-NET **production** certification — gateway provider issues this; we
  test the sandbox only
- SMS gateway integration — credentials not yet provisioned
- Multi-language UI (Arabic translation of labels) — phase 2
- Load testing beyond 50 concurrent users — phase 2
- Penetration testing by external firm — phase 2 (recommended)
- Disaster recovery / failover drills — phase 2

### 2.3 Assumptions

- ALRTAM provides at least **2 business testers** for the UAT cycle
- The SmarterASP demo environment remains available and isolated from
  any production data
- Test data (a seeded DB with sample properties, tenants, leases) is
  available throughout the testing window
- Gemini API key for Civil ID enrichment is funded by ALRTAM
- The customer's local network can reach K-NET's sandbox endpoint

---

## 3. Test approach

### 3.1 Levels of testing

| Level             | Who runs it           | When               | Output                            |
| ----------------- | --------------------- | ------------------ | --------------------------------- |
| **Unit tests**    | Vendor dev (automated) | Every commit       | xUnit pass/fail per build         |
| **Integration**   | Vendor dev (automated) | Every commit       | EF + SQL Server in-memory pass    |
| **E2E (smoke)**   | Vendor dev (automated) | Pre-deploy         | Playwright suite pass/fail        |
| **System tests**  | Test Lead (manual)    | After each build   | [TEST_CASES.md](TEST_CASES.md) — full pack |
| **UAT**           | ALRTAM business users | After system tests | Sign-off form (Appendix C)        |
| **Regression**    | Test Lead             | Before each release| Smoke pack (TESTER_GUIDE Cheat sheet) |

### 3.2 Types of testing

| Type                 | Method                                                              |
| -------------------- | ------------------------------------------------------------------- |
| **Functional**       | Manual against test cases; automated where stable                   |
| **UI / UX**          | Manual cross-browser, mobile-responsive checks                      |
| **Security**         | Role-boundary tests, XSS/SQLi probes, OWASP Top-10 review           |
| **Performance**      | Page-load timing, list-render with 500+ rows, job execution time    |
| **Localisation**     | KWD precision, Arabic input, Kuwait date format (DD/MM/YYYY)        |
| **Compatibility**    | Cross-browser matrix (Section 4.2)                                  |
| **Recovery**         | Hangfire retry, idempotency, app restart preserves state            |
| **Data integrity**   | Audit log completeness, no orphan records, FK constraints          |
| **Acceptance (UAT)** | Business-flow scenarios run by ALRTAM staff                         |

### 3.3 Automated vs manual split

- **Unit + Integration** — fully automated, in CI on every commit
- **E2E smoke** — automated via Playwright (`PropertyCreationTests`,
  `UnitCreationTests`, `LeaseCreationTests` already exist)
- **System tests** — manual, ~80% of test cases
- **UAT** — 100% manual by ALRTAM users

---

## 4. Environments

### 4.1 Test environments

| Environment      | URL                                       | DB                      | Purpose                       |
| ---------------- | ----------------------------------------- | ----------------------- | ----------------------------- |
| **Dev / local**  | http://localhost:5032                     | `RAVIWST\SQLEXPRESS\RentCollectionDB` | Developer iteration |
| **Demo**         | http://<deploy-host>      | `<database>`    | Vendor system tests + UAT     |
| **Pre-prod**     | TBD (subdomain on SmarterASP, separate DB) | Separate restored DB    | Final regression before go-live |
| **Production**   | TBD (alrtam.com.kw or similar)            | Separate restored DB    | Go-live                       |

### 4.2 Browser matrix

| Browser            | Version    | Priority | Notes                       |
| ------------------ | ---------- | -------- | --------------------------- |
| Chrome             | Latest 2   | P0       | Primary staff browser       |
| Edge               | Latest 2   | P0       | Common on Windows desktops  |
| Firefox            | Latest     | P1       | Some staff prefer it        |
| Safari             | iOS 16+    | P1       | Tenant portal on iPhones    |
| Chrome             | Android 12+| P1       | Tenant portal on Android    |

### 4.3 Test data

- A seeded `.bak` file ([RentCollectionDB_2026-06-04.bak](publish/RentCollectionDB_2026-06-04.bak))
  containing 1 admin, 1 tenant, sample properties + units
- Test PACI / Civil IDs generated to pass checksum (provided by Test Lead)
- Test K-NET cards documented in K-NET sandbox docs
- Test files: a small PDF, a JPG, a PNG (for upload tests)

---

## 5. Entry & exit criteria

### 5.1 Entry criteria (before testing can start)

- [ ] Build deployed to demo environment without 500 errors
- [ ] Smoke pack from TESTER_GUIDE Part 1 passes
- [ ] Test database restored and accessible
- [ ] Test accounts seeded and verified working
- [ ] Test environment URL shared with all testers
- [ ] Bug-tracker (or shared sheet) set up and accessible

### 5.2 Exit criteria (before promoting to UAT)

- [ ] 100% of P0 test cases passed
- [ ] ≥ 95% of P1 test cases passed
- [ ] ≥ 80% of P2 test cases passed (remaining either fixed or
      formally deferred with customer agreement)
- [ ] 0 open P0 defects
- [ ] ≤ 3 open P1 defects, each with mitigation/workaround
- [ ] No data-loss bugs of any priority
- [ ] No security bugs of P0 or P1
- [ ] All automated tests green in CI

### 5.3 UAT exit criteria (before go-live)

- [ ] All UAT scenarios (Appendix B) executed by an ALRTAM user
- [ ] All P0 + P1 scenarios marked Pass
- [ ] Signed UAT acceptance form (Appendix C)
- [ ] Production runbook handed over and verified
- [ ] Backup + restore procedure tested on production DB
- [ ] Customer training session completed (separate doc)

---

## 6. Schedule

Indicative — owner to firm up dates with customer.

| Phase                      | Duration | Owner                  | Output                          |
| -------------------------- | -------- | ---------------------- | ------------------------------- |
| Test plan sign-off         | 1 day    | Test Lead + Customer   | This document approved          |
| Test environment setup     | 2 days   | Vendor                 | Demo URL stable, DB restored    |
| Smoke + entry verification | ½ day    | Test Lead              | Entry criteria checked off      |
| System test cycle 1        | 5 days   | Test Lead              | TEST_CASES.md fully run         |
| Bug fix sprint             | 3 days   | Vendor dev             | All P0 fixed, P1 triaged        |
| System test cycle 2 (regression) | 2 days | Test Lead         | Exit criteria met               |
| UAT preparation            | 1 day    | Test Lead + Customer   | UAT script + accounts ready     |
| UAT execution              | 3 days   | ALRTAM users           | UAT sign-off form completed     |
| UAT defect fix             | 2 days   | Vendor dev             | UAT bugs resolved               |
| Pre-prod regression        | 1 day    | Test Lead              | Final go/no-go                  |
| Production deploy + smoke  | ½ day    | Vendor + Customer      | Live system                     |
| **Total**                  | **~21 working days** | | |

---

## 7. Roles & responsibilities

| Role                  | Name (TBA)   | Responsibilities                                            |
| --------------------- | ------------ | ----------------------------------------------------------- |
| **Project Sponsor**   | ALRTAM Ops Head | Signs off acceptance, allocates UAT users                |
| **Vendor PM**         | Sphears      | Schedule, escalation, change requests                       |
| **Test Lead**         | TBA          | Owns this plan, executes system tests, manages bugs         |
| **Vendor Dev**        | Ravi Shankar | Fixes bugs, deploys builds                                  |
| **ALRTAM UAT users**  | TBA × 2      | Runs UAT scenarios, signs off acceptance                    |
| **DBA (informal)**    | Vendor       | Restores backups, manages SmarterASP DB                     |
| **Customer success**  | TBA          | Trains end users post-deploy                                |

---

## 8. Risk register

| #  | Risk                                                    | Likelihood | Impact | Mitigation                                                                          |
| -- | ------------------------------------------------------- | ---------- | ------ | ----------------------------------------------------------------------------------- |
| 1  | K-NET production certification delayed                  | Medium     | High   | Test sandbox fully; have manual cash-receipt fallback for go-live                   |
| 2  | Gemini API key exhausts quota during testing            | Low        | Medium | Use Gemini sparingly; have manual Civil ID entry path always available             |
| 3  | SmarterASP performance during demo                      | Medium     | Medium | Pre-prod uses dedicated DB; can fail-over to local-hosted if needed                |
| 4  | UAT users unavailable                                   | Medium     | High   | Confirm UAT users + dates in writing before system tests start                     |
| 5  | Production DB migration drops data                      | Low        | Critical | Mandatory backup before migration; restore drill in pre-prod                     |
| 6  | Browser-specific UI bugs (Safari)                       | Medium     | Low    | Cross-browser matrix runs early; capture screenshots                                |
| 7  | Arabic text breaks list/PDF layout                      | Medium     | Medium | Dedicated localization test pack with Arabic-only data                              |
| 8  | Hangfire jobs run on demo affect customer "live" feel   | Low        | Low    | Trigger jobs manually only; document daily UTC schedule clearly                     |
| 9  | Security finding discovered late                        | Medium     | High   | OWASP review during Sprint 23 (not at UAT); engage external auditor in phase 2     |
| 10 | Customer requests scope change mid-cycle                | High       | Medium | Change-request log; impact analysis before accepting; reset exit criteria           |

---

## 9. Defect management

### 9.1 Severity levels

| Severity | Definition                                                          | Resolution SLA |
| -------- | ------------------------------------------------------------------- | -------------- |
| **P0 — Critical** | Blocks core workflow, data loss, security breach, system down | Same day       |
| **P1 — Major**    | Major function impaired but workaround exists                  | 3 business days |
| **P2 — Minor**    | Cosmetic, edge case, low-frequency                              | Next cycle     |
| **P3 — Trivial**  | Typo, alignment, suggestion                                     | Backlog        |

### 9.2 Bug lifecycle

```
NEW → TRIAGED → IN PROGRESS → FIXED → VERIFIED → CLOSED
                                      ↓
                                  REOPENED (if verification fails)
```

### 9.3 Bug report format

Every defect filed must include:
- Title (terse, problem-first)
- Test case ID (if related)
- Environment + browser
- Build hash (visible in `/Account/Login` footer)
- Steps to reproduce (numbered)
- Expected vs Actual
- Screenshot or video
- Log excerpt from `logs/stdout_*.log`
- Severity (proposed by reporter, confirmed by Test Lead at triage)

(Template included in [TESTER_GUIDE.md](TESTER_GUIDE.md) Section: "What to write down when you find a bug")

### 9.4 Triage

Test Lead + Vendor Dev run a triage session **daily** during system
testing, **twice daily** during UAT. Output is the day's bug fix list.

---

## 10. Test deliverables

By end of project:

1. **This Test Plan** (signed)
2. **Executed [TEST_CASES.md](TEST_CASES.md)** — one copy per test cycle, with results filled in
3. **UAT sign-off form** (Appendix C)
4. **Bug log** — exported from tracker, all bugs with final status
5. **Test summary report** — pass/fail counts, defect statistics, residual risks
6. **Lessons-learned note** — short retrospective for next phase

---

## 11. Tools

| Purpose            | Tool                          | Notes                                  |
| ------------------ | ----------------------------- | -------------------------------------- |
| Unit tests         | xUnit + Moq                   | In `RentCollection.Tests`              |
| Integration tests  | EF Core in-memory + SQL Express | In `RentCollection.IntegrationTests` |
| E2E tests          | Playwright + xUnit            | In `RentCollection.E2ETests`           |
| Test management    | TEST_CASES.md (Markdown) or Excel | Or migrate to TestRail later         |
| Bug tracking       | GitHub Issues *or* shared Excel | Customer-preferred                   |
| Screenshots        | Windows Snip & Sketch (Win+Shift+S) | Or any cross-platform tool         |
| Browser testing    | Real devices + BrowserStack (optional) | iOS / Android coverage             |
| Performance        | Browser DevTools Lighthouse   | Manual; thresholds in TEST_CASES Sec.14 |
| Logs               | FTP to `/logs/stdout_*.log`    | SmarterASP, see SYSTEM_WORKFLOW Sec.12 |

---

## 12. Sign-off

This Test Plan is the contract for testing this delivery. Changes
during execution require a written change request and a re-signed
addendum.

| Role                    | Name      | Signature | Date       |
| ----------------------- | --------- | --------- | ---------- |
| Vendor PM               |           |           |            |
| Vendor Test Lead        |           |           |            |
| Customer Sponsor (ALRTAM) |         |           |            |
| Customer Test Lead (ALRTAM) |       |           |            |

---

## Appendix A — Test case index

The full test case catalogue is maintained in [TEST_CASES.md](TEST_CASES.md).
Summary breakdown:

| Section                       | Cases | P0 | P1 | P2 |
| ----------------------------- | ----- | -- | -- | -- |
| 0. Smoke pack                 | 7     | 5  | 2  | 0  |
| 1. Authentication             | 10    | 5  | 4  | 1  |
| 2. Property module            | 11    | 4  | 7  | 0  |
| 3. Unit module                | 5     | 2  | 3  | 0  |
| 4. Tenant + Civil ID          | 14    | 5  | 8  | 1  |
| 5. Lease module               | 12    | 4  | 7  | 1  |
| 6. Invoices                   | 6     | 3  | 3  | 0  |
| 7. Payments (Cash / Bank / K-NET) | 11 | 6  | 5  | 0  |
| 8. Maintenance                | 5     | 2  | 2  | 1  |
| 9. Documents                  | 6     | 0  | 5  | 1  |
| 10. Reports                   | 5     | 1  | 4  | 0  |
| 11. Tenant portal             | 8     | 5  | 3  | 0  |
| 12. Role-based access         | 10    | 7  | 3  | 0  |
| 13. Edge cases                | 10    | 3  | 6  | 1  |
| 14. Performance               | 6     | 0  | 6  | 0  |
| **Totals**                    | **126** | **52** | **68** | **6** |

---

## Appendix B — UAT scenarios (customer-facing)

These are business-flow narratives, not click-by-click test cases.
Each must be executed by an ALRTAM business user with their own
words/data.

| ID    | Scenario                                                                                          | Acceptance criteria                                          |
| ----- | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| UAT-01 | "I am a manager onboarding a new building. I create the property, add 10 units, and assign a building manager." | Property + 10 units + manager linked, no errors |
| UAT-02 | "I am a manager onboarding a new tenant. I use Civil ID scan to fill the form, attach their Civil ID document, and create their lease in one sitting." | Tenant + Lease + Documents all created in ≤ 5 minutes |
| UAT-03 | "I am an accountant. A tenant has paid by bank transfer. I review the screenshot and approve."    | Payment moves from Pending to Confirmed, receipt sent        |
| UAT-04 | "I am a tenant. I log in, see my next rent due, pay by K-NET, and download my receipt."           | All four actions complete within the portal                  |
| UAT-05 | "I am a tenant. I report a leak in my kitchen, upload a photo. A week later the manager marks it resolved." | Full request lifecycle visible to tenant      |
| UAT-06 | "I am an admin. I export this month's rent collection report to Excel and verify the totals match my own ledger." | Excel matches expected numbers, KWD 3-decimal precision |
| UAT-07 | "I am an admin. I terminate a lease early due to tenant default. The system handles the refund calculation and stops future invoicing." | Lease Terminated, refund queued, no further invoices |
| UAT-08 | "I am a tenant. I try to view another tenant's invoice by guessing the URL — the system blocks me." | 403 or 404, never the other tenant's data    |
| UAT-09 | "I am an admin. I attempt to delete a property that still has active leases. The system blocks me with a clear message." | Friendly block + explanation, never a 500 error |
| UAT-10 | "I am an Arabic-named tenant (محمد العلي). My name displays correctly everywhere — dashboard, invoice PDF, email receipt." | Arabic renders correctly across surfaces        |

---

## Appendix C — UAT acceptance sign-off

To be completed at end of UAT cycle.

```
PROJECT:         RentCollection (ALRTAM Property Management)
BUILD HASH:      ____________________________
UAT WINDOW:      from ____________ to ____________
ENVIRONMENT:     ____________________________

SCENARIOS EXECUTED:    ____ of ____
SCENARIOS PASSED:      ____
SCENARIOS FAILED:      ____    (must be 0 to sign off)
DEFERRED ITEMS:        attached as Annex (if any)

The undersigned customer representatives confirm that the system has
been tested against the agreed UAT scenarios and is accepted for
production deployment, subject to the deferred items list (if any).


Customer Sponsor      : ________________________
                        Name + Title + Signature + Date

Customer Test Lead    : ________________________
                        Name + Title + Signature + Date

Vendor PM             : ________________________
                        Name + Title + Signature + Date
```

---

## Appendix D — Production go-live checklist

To be completed by Vendor PM + Customer Test Lead on go-live day.

- [ ] Pre-prod regression pack passed within the last 24 hours
- [ ] Production database backup taken **immediately before** deploy
- [ ] Production `appsettings.json` reviewed: no dev/demo credentials
- [ ] Production secrets set as environment variables (not in source)
- [ ] `Demo:LoginEnabled` set to **false** in production
- [ ] HTTPS / Let's Encrypt cert provisioned and verified
- [ ] DNS pointed to production URL
- [ ] Hangfire schedule reviewed (no test jobs left enabled)
- [ ] Smoke pack (SMK-01 to SMK-07) passes on production URL
- [ ] First real user (admin) successfully logged in
- [ ] Roll-back procedure printed and at hand
- [ ] Support contact list distributed to customer

---

*Plan version: 1.0 · 2026-06-09*
*Next review: after Test Cycle 2 completes; update with actuals.*
