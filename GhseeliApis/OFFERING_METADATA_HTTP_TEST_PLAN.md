# Offering metadata HTTP test plan

## Observable behavior

Business Owners and Admins can create and update service offerings with:

- optional Arabic and Hebrew qualifier text;
- an optional controlled badge code, initially `MostRequested`.

Arabic qualifier text is returned for Arabic Customer catalog reads. Hebrew
qualifier text is returned for Hebrew reads when present and falls back to the
Arabic qualifier when omitted. The badge code is language-independent and is
serialized as a string. Clearing the fields with `null` removes the persisted
values. Employee and cross-company authorization behavior is unchanged.

## Code Test Plan

- Business FluentValidation tests for qualifier null/blank normalization and
  200-character boundaries.
- Business mapper and service tests for create, update, clear, list, and detail
  propagation.
- Business relational repository tests for persistence and catalog-version
  increments on metadata-only changes.
- Integration-contract serialization tests for exact camel-case field names,
  `MostRequested` string serialization, nulls, and integer-enum rejection.
- Internal catalog publication tests for exact metadata propagation.
- Customer read-model relational tests for apply, update, clear, rollback, and
  stable local IDs.
- Customer service and HTTP tests for Arabic, Hebrew, Arabic fallback, null
  qualifiers, list/detail consistency, and language-independent badges.
- Business and Customer clean/populated migration tests.
- Demo fixture and Business/Customer parity tests.

## HTTP Test Plan

- HTTP required: Yes
- Why: Business request/response DTOs, validation, Swagger schemas, internal
  snapshot serialization, and Customer catalog response DTOs change.
- Execution level: TestServer and live local. Never run these scenarios against
  Production.

| Scenario ID | Surface | Request | Expected result | Status |
|---|---|---|---|---|
| `OFFERING-METADATA-BUSINESS-001` | Business create | Owner `POST /api/v1/business/catalog/offerings` with both qualifiers and `MostRequested` | `201`; exact normalized fields; catalog version increases | passed - TestServer |
| `OFFERING-METADATA-BUSINESS-002` | Business create | Admin creates an offering for an explicitly selected company | `201`; exact metadata returned | passed - existing authorization plus service coverage |
| `OFFERING-METADATA-BUSINESS-003` | Business authorization | Employee attempts offering create/update | `403`; no mutation or version increment | passed - existing endpoint authorization regression |
| `OFFERING-METADATA-BUSINESS-004` | Business ownership | Owner targets an offering outside the assigned company | ownership-safe rejection; no mutation | passed - existing ownership regression |
| `OFFERING-METADATA-BUSINESS-005` | Business validation | Qualifier exceeds 200 characters | `400`; field validation error; no mutation | passed - validator boundary tests |
| `OFFERING-METADATA-BUSINESS-006` | Business model binding | Unknown or integer badge value | `400`; no mutation | passed - TestServer |
| `OFFERING-METADATA-BUSINESS-007` | Business update | Owner changes qualifier and badge | `200`; exact updated metadata; catalog version increases | passed - service and TestServer |
| `OFFERING-METADATA-BUSINESS-008` | Business clear | Owner sends `null` qualifier and badge | `200`; fields are `null` on subsequent read/list | passed - service and TestServer |
| `OFFERING-METADATA-INTERNAL-009` | Internal snapshot | HMAC-authenticated catalog snapshot | Exact qualifier and string badge values are published | passed - TestServer |
| `OFFERING-METADATA-CUSTOMER-010` | Customer Arabic list | Device-authenticated business offering list in Arabic | Arabic qualifier and `MostRequested` returned | passed - TestServer and live local |
| `OFFERING-METADATA-CUSTOMER-011` | Customer Hebrew list | Hebrew qualifier exists | Hebrew qualifier returned | passed - TestServer and live local |
| `OFFERING-METADATA-CUSTOMER-012` | Customer Hebrew fallback | Hebrew qualifier absent, Arabic exists | Arabic qualifier returned with response language `he` | passed - TestServer and live local |
| `OFFERING-METADATA-CUSTOMER-013` | Customer null metadata | Both qualifiers and badge absent | All optional fields are `null` | passed - TestServer and Demo relational fixture |
| `OFFERING-METADATA-CUSTOMER-014` | Customer detail | Read the same offering through detail endpoint | Metadata matches list response | passed - TestServer |
| `OFFERING-METADATA-CONTRACT-015` | Swagger | Read both OpenAPI documents | Qualifier nullability and string badge enum are documented | passed - TestServer; live assertion added |

## Coverage categories

| Category | Coverage |
|---|---|
| Happy path | Business create/update/read/list and Customer list/detail |
| Malformed payload/model binding | Unknown and integer badge values |
| Authn/authz/ownership | Owner, Admin, Employee, cross-company Owner, device token |
| Validation boundaries | Qualifier null, blank, 200, and 201 characters |
| State/version/concurrency | Metadata-only updates increment catalog version; normal concurrency behavior retained |
| Failure/unavailability | Existing refresh failure and stale-snapshot coverage remains applicable |
| Deletion/read-after-delete | Existing offering deletion coverage remains applicable; metadata introduces no new delete behavior |
| Contract/Swagger | Business request/response and Customer response schemas |
| Localization | Arabic, Hebrew, and Hebrew-to-Arabic qualifier fallback |
| Security/no leakage | No new secrets or PII; rejected payloads do not echo raw bodies |

## HTTP Test Results

- Full automated run: 1,965 passed, 0 failed, 0 skipped
  (Customer 1,332; Business 609; Demo 24).
- Release build: 0 warnings, 0 errors.
- Step 17 asset validation: passed with 1,167 inherited selections.
- Customer Step 9 live-local preseeded manifest: 19/19 passed, including
  `STEP9-OFFERING-METADATA-FALLBACK-022`.
- Complete Step 17 live-local gate: passed on 2026-09-22 with all 50 active
  provider-neutral Step 17 scenarios and all 1,167 inherited scenarios,
  including Step 9 metadata/fallback, Step 12 booking confirmation, Step 15
  localization/Swagger and a stale-to-fresh cross-process Demo catalog
  refresh, Step 16 schema isolation/adverse-schema probes, and final
  process/database cleanup.
- Eight retired Stripe scenarios remain preserved as historical coverage and
  are intentionally excluded from execution after the Lahza migration.
- Production deployment run `35740412333` succeeded on commit `ce669c7`,
  including both migrations, both deployments, and hosted health checks.
- Hosted Demo seed run `35742511947` succeeded. Follow-up API verification
  passed for all 5 businesses and 20 offerings in Arabic and Hebrew, including
  fallback, `MostRequested`, null fixtures, list/detail parity, non-stale
  catalogs, deterministic Demo-only source IDs, Demo logins, and deployed
  Business/Customer Swagger metadata.

## Final live-local checklist

Run only against disposable local Customer and Business databases.

1. Apply the Business
   `AddBusinessOfferingPresentationMetadata` migration and Customer
   `AddCustomerOfferingPresentationMetadata` migration. Verify nullable
   `nvarchar(200)` qualifier columns and nullable `nvarchar(50)` badge columns.
2. Start both APIs with Swagger enabled and matching internal HMAC
   configuration. Create an Owner, Admin, Employee, foreign-company Owner, two
   companies, and owned branch/category fixtures. Register a Customer device.
3. Record the owning company catalog version, then create an offering as Owner
   with whitespace-padded Arabic/Hebrew qualifiers and
   `"badgeCode":"MostRequested"`. Expect `201`, trimmed values, a string badge,
   and an exact one-version increment.
4. Read the Business offering through list and detail in Arabic and Hebrew.
   Expect the selected qualifier language only and the same language-independent
   badge.
5. Create a metadata-bearing offering as Admin for an explicit company.
   Expect `201`, correct ownership, and a version increment only for that
   company.
6. Attempt create and update as Employee. Attempt read/update as the
   foreign-company Owner. Expect `403` for Employee and ownership-safe `404`
   for the foreign Owner, with no row, field, or catalog-version mutation.
7. Update the Owner offering to different non-null qualifier values. Expect
   normalization, exact persistence, matching list/detail responses, and one
   version increment. Then clear all metadata with explicit `null` values and
   expect SQL/API nulls plus one version increment.
8. Submit 201-character Arabic and Hebrew qualifiers, unknown badge
   `"Popular"`, and numeric badge `0`. Expect `400` for each, no mutation, and
   no version increment.
9. Call the internal snapshot without authentication, with an invalid
   signature, and with a stale timestamp. Expect `401` and no catalog leakage.
   Call again with the canonical signer and expect `200`, the current version,
   exact qualifiers, and string-or-null badge values.
10. Trigger a Customer refresh and read list/detail in Arabic. Expect the
    Arabic qualifier, `MostRequested`, matching list/detail metadata,
    `Cache-Control: no-store`, and stable Customer-local IDs distinct from
    Business source IDs.
11. With both qualifiers populated, read list/detail in Hebrew and expect the
    Hebrew qualifier. Clear only Hebrew, publish and refresh, then expect the
    Arabic qualifier as fallback while response language remains `he`.
12. Clear both qualifiers and the badge, publish and refresh, then read Arabic
    and Hebrew list/detail. Expect explicit JSON nulls and no stale metadata.
13. Apply a controlled same-version snapshot with changed metadata. Expect the
    changed hash to force persistence while preserving Customer-local IDs.
    Verify missing and malformed Customer device tokens still return `401`.
14. Inspect both Swagger documents. Business create/update requests and the
    Customer offering response must document nullable qualifier/badge fields;
    `CatalogOfferingBadgeCode` must be a string enum containing only
    `MostRequested`. Verify Business/Customer database parity, then stop both
    APIs and drop the disposable databases.
