# Step 15 HTTP Test Plan — Localization, Stable Errors, and Complete Swagger

Status: **frozen before Step 15 production changes**

HTTP required: **Yes** — Step 15 changes cross-cutting model binding,
authentication/authorization failures, localization, response headers, and the
OpenAPI contract of two independently hosted APIs.

This plan follows `HTTP_TEST_PLAN_STANDARD.md`. IDs are permanent and may only
be appended. Nothing in this document authorizes production execution.

## 1. Frozen observable contract

### 1.1 Scope and host boundaries

- Customer modern routes are `/api/v1/*`; Customer legacy routes remain
  `/api/*`; Stripe remains `/api/stripe/webhook`.
- Business owner/staff routes are `/api/v1/business/*`; service routes are
  `/api/v1/internal/*`.
- Customer and Business Swagger documents must each be independently usable:
  no implementation-project reference, foreign database, foreign JWT, or
  undeclared shared schema is needed.
- Customer JWTs never authenticate Business routes. Business JWTs never
  authenticate Customer or either host's internal routes. HMAC never
  authenticates user routes.
- Step 15 does not change order, selection, pricing, idempotency, payment, or
  booking semantics frozen in Steps 10–14. It documents and regression-tests
  them.

### 1.2 Language selection

For localized Customer and Business user-facing responses:

1. Presence of a `language` query is authoritative where the operation
   documents it. Only a single nonblank `ar` or `he` value is valid,
   case-insensitively and after trimming. Blank, duplicate, comma-delimited, or
   unsupported query values return `400 language_invalid`; they never fall back
   to a header.
2. Otherwise parse `Accept-Language` with quality values. Choose the supported
   range with greatest `q`, then wire order; `ar-*` maps to `ar`, `he-*` maps
   to `he`, wildcard and unsupported ranges do not select Hebrew.
3. Missing, blank, malformed, all-unsupported, wildcard-only, or all-`q=0`
   headers safely default to `ar`.
4. Success payload localization and Problem Details use the same selected
   language. `Content-Language` is exactly `ar` or `he`.
5. Internal HMAC and Stripe webhook problems are deliberately English and omit
   `language` and `Content-Language`. Health and Swagger are not localized.
6. Language changes presentation only: status, stable `code`, `type`,
   `fieldErrors` keys/codes, authorization, ownership, version, money,
   persistence, and idempotency remain identical.

### 1.3 Exact Problem Details envelope

Every application-generated failure on a Step 15-covered route returns
`application/problem+json` and exactly the applicable RFC 7807 members:

```json
{
  "type": "https://api.ghseeli.example/errors/{code}",
  "title": "<exact catalog title>",
  "status": 400,
  "detail": "<exact catalog detail>",
  "code": "<stable code>",
  "correlationId": "<same value as X-Correlation-Id>",
  "language": "ar",
  "fieldErrors": {
    "camelCaseField": ["<exact localized catalog message>"]
  }
}
```

`fieldErrors` is present only for field failures, keys are camelCase JSON paths
(including indexes), key order is ordinal, values are nonempty, deduplicated,
and deterministic. No framework exception/type name or attempted value is
returned. The exact Arabic/Hebrew title, detail, and field message for every
stable code is a contract snapshot; tests must compare full strings, not
`contains`. Existing Steps 8–14 code/detail pairs remain frozen. New
cross-cutting codes are:

| Status | Stable code | Meaning |
|---:|---|---|
| 400 | `language_invalid` | Explicit query language is invalid |
| 400 | `request_invalid` | General malformed/model/validation request |
| 401 | `customer_authentication_required` | Customer JWT missing/invalid |
| 403 | `customer_authorization_forbidden` | Customer JWT lacks policy/role |
| 401 | `business_authentication_required` | Business JWT missing/invalid |
| 403 | `business_authorization_forbidden` | Business role/assignment denied |
| 404 | `resource_not_found` | Safe general missing resource |
| 405 | `method_not_allowed` | Known route, wrong method |
| 409 | `request_conflict` | Safe general state/concurrency conflict |
| 413 | `request_body_too_large` | General bounded-body rejection |
| 415 | `unsupported_media_type` | JSON operation received another media type |
| 500 | `unexpected_error` | Unhandled safe failure; no internals |
| 503 | `service_unavailable` | Safe dependency/startup/runtime unavailability |

Feature-specific codes take precedence (`device_*`, `configuration_*`,
`catalog_*`, `checkout_*`, `pricing_*`, `booking_*`, `payment_*`,
`stripe_*`, `internal_*`, and Business domain codes). Internal HMAC keeps the
English Step 3/5/6/12/13 envelope and exact codes; webhook keeps its English
Step 14 envelope. Route misses generated before endpoint selection may use the
general `resource_not_found`; they must still be safe and correlated.

The generic catalog is frozen below. The title is exact and intentionally
shared within a language; detail is exact per code. Generic normalized
`fieldErrors` use exactly `القيمة غير صالحة.` / `הערך אינו חוקי.`; focused
validators retain their already-frozen code-specific messages.

| Code | Arabic title | Arabic detail | Hebrew title | Hebrew detail |
|---|---|---|---|---|
| `language_invalid` | `تعذر إكمال الطلب.` | `اللغة المطلوبة غير مدعومة.` | `לא ניתן להשלים את הבקשה.` | `השפה המבוקשת אינה נתמכת.` |
| `request_invalid` | `تعذر إكمال الطلب.` | `الطلب غير صالح.` | `לא ניתן להשלים את הבקשה.` | `הבקשה אינה חוקית.` |
| `customer_authentication_required` | `تعذر إكمال الطلب.` | `مطلوب تسجيل دخول العميل.` | `לא ניתן להשלים את הבקשה.` | `נדרש אימות לקוח.` |
| `customer_authorization_forbidden` | `تعذر إكمال الطلب.` | `لا يملك العميل صلاحية تنفيذ هذا الطلب.` | `לא ניתן להשלים את הבקשה.` | `ללקוח אין הרשאה לבצע בקשה זו.` |
| `business_authentication_required` | `تعذر إكمال الطلب.` | `مطلوب تسجيل دخول حساب العمل.` | `לא ניתן להשלים את הבקשה.` | `נדרש אימות לחשבון העסקי.` |
| `business_authorization_forbidden` | `تعذر إكمال الطلب.` | `لا يملك حساب العمل صلاحية تنفيذ هذا الطلب.` | `לא ניתן להשלים את הבקשה.` | `לחשבון העסקי אין הרשאה לבצע בקשה זו.` |
| `resource_not_found` | `تعذر إكمال الطلب.` | `المورد المطلوب غير موجود.` | `לא ניתן להשלים את הבקשה.` | `המשאב המבוקש לא נמצא.` |
| `method_not_allowed` | `تعذر إكمال الطلب.` | `طريقة HTTP غير مسموحة لهذا المسار.` | `לא ניתן להשלים את הבקשה.` | `שיטת HTTP אינה מותרת עבור נתיב זה.` |
| `request_conflict` | `تعذر إكمال الطلب.` | `يتعارض الطلب مع الحالة الحالية.` | `לא ניתן להשלים את הבקשה.` | `הבקשה מתנגשת עם המצב הנוכחי.` |
| `request_body_too_large` | `تعذر إكمال الطلب.` | `حجم نص الطلب يتجاوز الحد المسموح.` | `לא ניתן להשלים את הבקשה.` | `גוף הבקשה חורג מהמגבלה המותרת.` |
| `unsupported_media_type` | `تعذر إكمال الطلب.` | `نوع محتوى الطلب غير مدعوم.` | `לא ניתן להשלים את הבקשה.` | `סוג התוכן של הבקשה אינו נתמך.` |
| `unexpected_error` | `تعذر إكمال الطلب.` | `حدث خطأ غير متوقع.` | `לא ניתן להשלים את הבקשה.` | `אירעה שגיאה בלתי צפויה.` |
| `service_unavailable` | `تعذر إكمال الطلب.` | `الخدمة غير متاحة مؤقتًا.` | `לא ניתן להשלים את הבקשה.` | `השירות אינו זמין זמנית.` |

All application problems and all sensitive/mutable successes carry
`Cache-Control: no-store`; no failure carries `ETag`, `Last-Modified`, or
`Set-Cookie`. Every response carries a bounded `X-Correlation-Id`. A valid
caller value is echoed; missing/invalid/CRLF/overlong values are replaced.
Localized responses carry `Vary: Accept-Language` without erasing existing
`Vary` values. Both hosts apply `X-Content-Type-Options: nosniff`,
`X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, a restrictive
`Permissions-Policy`, and a restrictive Content Security Policy to API
responses. Swagger UI gets the narrow separate CSP it needs and no broader
permission. Production HTTPS responses carry configured HSTS; development HTTP
does not.
Problems never reveal stack/SQL/provider response, JWT, device token, HMAC
headers/signature/secret/nonce, idempotency key, OAuth code, Stripe secret/raw
body, PII, internal row IDs, or attempted sensitive values.

### 1.4 Swagger contract

Each `/swagger/v1/swagger.json` must be valid OpenAPI 3, deterministic, and:

- list every owned route exactly once with the runtime verb and no route from
  the other API;
- provide operation ID, summary/description, tags, request/response schemas,
  formats, requiredness/nullability, bounds, enums, defaults, examples, and
  every documented runtime status with `application/problem+json`;
- define reusable `ProblemDetails` plus `fieldErrors`, correlation and
  localization headers;
- define Customer HTTP-bearer JWT, Business HTTP-bearer JWT, `X-Device-Token`, internal
  HMAC headers, `Idempotency-Key`, `X-Order-Guid`, and Stripe signature only
  where applicable;
- attach security per operation, never globally: anonymous operations have
  none; device-only operations require only device; JWT+device require both;
  internal operations require the complete HMAC scheme and no JWT;
- include safe examples with placeholders and no real credential, token,
  signature, secret, PII, internal ID, or production host;
- advertise JSON input only where runtime requires JSON and accurately
  document 64 KiB/body/header limits, language precedence, no-store,
  idempotency/replay, ownership masking, and payment capability semantics.

Swagger is enabled in Development or explicit non-production configuration;
disabled production/default environments return 404.

## 2. Execution and fixtures

| Level | Purpose and rationale |
|---|---|
| **Live-local** | Both real Kestrel hosts, Swagger availability, content negotiation, headers, wrong verbs, request-size boundary, cross-host token rejection, and HMAC/HTTPS behavior. Host/proxy behavior is material. |
| **TestServer** | Exhaustive endpoint matrix with fixed clocks, fake auth/dependencies, isolated stores, exact full Problem Details, language negotiation, and side-effect assertions. |
| **Contract** | Parse both OpenAPI JSON documents and assert routes, operation security, schemas, examples, headers, status/media types, and cross-host exclusion. |
| **Automated-only** | Translation/resource snapshots, log capture/redaction, deterministic ordering, concurrency/failpoints, and code-to-HTTP mapping where live execution would be unsafe or nondeterministic. |

Required future fixtures (do **not** create a manifest in this planning step):

- dedicated local Customer and Business SQL databases; disposable Arabic and
  Hebrew catalog/company/branch/offering/add-on records;
- Customer User/Admin JWTs, Business Owner/Employee/Admin JWTs, expired and
  wrong-issuer tokens, current/expired/rotated device tokens;
- HMAC clients per allowed operation, fixed UTC clock, active/next secrets,
  fresh/replayed nonces, and deterministic idempotency keys;
- device-owned draft, priced snapshot, order GUID, confirmed booking, payment,
  reservation/work order/outbox records, plus foreign-owner/device records;
- fake unavailable dependencies and persistence failpoints; Stripe locally
  signed bodies only (no production or real charge);
- full exact localization oracle keyed by `(code, language, field-code)`.

Two manifests will be required when implementation starts:
`step-15-customer-docs-localization.manifest.json` and
`step-15-business-docs-localization.manifest.json`. They must use the IDs below,
local placeholders, runtime-generated secrets/nonces, and idempotent cleanup.

### Scenario-schema defaults

The compact rows below inherit these required standard fields:

- `featureStep`: `STEP_15`;
- `authIdentity`: stated in the row or the route's identity in §4;
- `prerequisitesSetup`: the isolated fixture set above, narrowed to the route;
- `requestHeaders`/`requestBody`: the named variation plus otherwise-valid
  route input; implementation tests/manifests must record the exact JSON and
  relevant headers rather than infer them;
- `expectedStatus`/`expectedStableErrorCode`: stated in the row, or the
  pre-Step-15 feature contract for a success/domain regression;
- `responseAssertions`/`headerAssertions`: the row plus all applicable frozen
  assertions in §§1.2–1.4;
- `dataVersionSideEffects`: no side effect on rejection; on success, exactly
  the route's pre-Step-15 transaction/idempotency/version behavior;
- `cleanup`: delete only that scenario's uniquely prefixed disposable records;
- `automation`: `automated-live`, `automated-testserver`, or contract/unit
  automation corresponding to `Level`;
- `status`: `planned`; `resultEvidence`: `none yet`;
- `deferredRationale`: `none`. Any later deferral must identify owner,
  unblock condition, compensating test, and non-shipping rationale.

## 3. Stable scenarios

In every failure row, “exact problem” means full equality to §1.3 including
status/code/type/title/detail/language applicability/fieldErrors,
content-type, correlation, cache headers, and redaction. A mutating rejection
also asserts zero domain/idempotency/nonce/provider side effects unless the
route's earlier frozen replay contract says otherwise.

### A. Cross-cutting language and errors

| ID | Level | Request / expected result |
|---|---|---|
| `STEP15-LANG-QUERY-AR-001` | TestServer + live-local | Representative localized read/error with `?language=ar` and Hebrew header: exact Arabic output/problem and `Content-Language: ar`. |
| `STEP15-LANG-QUERY-HE-002` | TestServer + live-local | `?language=he` and Arabic header: exact Hebrew output/problem and `Content-Language: he`. |
| `STEP15-LANG-QUERY-CASE-003` | TestServer | Trimmed/case variants normalize to `ar`/`he`. |
| `STEP15-LANG-QUERY-UNSUPPORTED-004` | TestServer + live-local | `language=en`: 400 exact `language_invalid`, Arabic-safe presentation, no fallback/side effect. |
| `STEP15-LANG-QUERY-DUPLICATE-005` | TestServer | Duplicate/mixed query values: same exact 400. |
| `STEP15-LANG-QUERY-EMPTY-006` | TestServer | Present empty/whitespace query returns exact 400 `language_invalid`; header cannot rescue it. |
| `STEP15-LANG-HEADER-AR-007` | TestServer | `Accept-Language: ar`: exact Arabic success/problem. |
| `STEP15-LANG-HEADER-HE-008` | TestServer | `he-IL`: exact Hebrew success/problem. |
| `STEP15-LANG-HEADER-Q-009` | TestServer | Weighted supported ranges select greatest positive q. |
| `STEP15-LANG-HEADER-TIE-010` | TestServer | Equal q selects first supported wire range deterministically. |
| `STEP15-LANG-HEADER-QZERO-011` | TestServer | Supported q=0 is unacceptable and Arabic default is used. |
| `STEP15-LANG-HEADER-WILDCARD-012` | TestServer | Wildcard-only safely defaults Arabic. |
| `STEP15-LANG-HEADER-MALFORMED-013` | TestServer + live-local | Malformed q/comma/range never 500; defaults Arabic. |
| `STEP15-LANG-HEADER-OVERSIZE-014` | TestServer + live-local | Oversized language header is bounded/rejected safely, never logged. |
| `STEP15-LANG-DEFAULT-015` | TestServer + live-local | No selector: Arabic and `Content-Language: ar`. |
| `STEP15-LANG-INVARIANCE-016` | Automated-only | Arabic/Hebrew paired calls differ only in localized presentation fields. |
| `STEP15-LANG-INTERNAL-EXEMPT-017` | TestServer | HMAC problem ignores query/header, stays English, and omits language headers/member. |
| `STEP15-LANG-WEBHOOK-EXEMPT-018` | TestServer | Stripe problem has the same nonlocalized behavior. |
| `STEP15-LANG-HEALTH-SWAGGER-EXEMPT-019` | Contract + live-local | Health/Swagger are not localized and do not advertise language. |
| `STEP15-PROBLEM-MODEL-BINDING-020` | TestServer | Invalid GUID/enum/date/JSON produces exact `request_invalid`, normalized fieldErrors, never framework text. |
| `STEP15-PROBLEM-FLUENT-VALIDATION-021` | TestServer | Multiple Business validation failures are ordered, camelCase, localized, deduplicated. |
| `STEP15-PROBLEM-WRONG-CONTENT-TYPE-022` | TestServer + live-local | Every JSON mutation family returns exact 415 feature/general code before handler. |
| `STEP15-PROBLEM-MISSING-BODY-023` | TestServer | Missing/empty/null body returns exact 400 and no mutation. |
| `STEP15-PROBLEM-BODY-BOUNDARY-024` | TestServer + live-local | 65,536 bytes reaches binding; 65,537/content-length/chunked returns exact 413. |
| `STEP15-PROBLEM-WRONG-METHOD-025` | TestServer + live-local | Known route wrong verb returns exact 405, correct `Allow`, correlation/no-store, no mutation. |
| `STEP15-PROBLEM-UNKNOWN-ROUTE-026` | TestServer + live-local | Unknown route returns safe correlated 404, never endpoint/domain detail. |
| `STEP15-PROBLEM-UNHANDLED-027` | TestServer | Injected exception returns exact 500 `unexpected_error`, no internals. |
| `STEP15-PROBLEM-UNAVAILABLE-028` | TestServer | Dependency failure maps exact feature code or `service_unavailable`, never empty 200. |
| `STEP15-PROBLEM-CORRELATION-ECHO-029` | TestServer + live-local | Valid correlation echoes in header/body and forwarded internal call. |
| `STEP15-PROBLEM-CORRELATION-REPLACE-030` | TestServer + live-local | Missing, blank, CRLF, comma, and overlong correlation are replaced by safe bounded values. |
| `STEP15-PROBLEM-CACHE-031` | TestServer + live-local | Every problem and sensitive success is `no-store`; no validator/cookie headers. |
| `STEP15-PROBLEM-REDACTION-032` | Automated-only | Captured HTTP and logs exclude every secret/PII/internal value listed in §1.3. |
| `STEP15-PROBLEM-TYPE-CODE-033` | Contract + TestServer | Every stable code maps exactly to `/errors/{code}` and is language-invariant. |
| `STEP15-PROBLEM-FIELD-ORDER-034` | Automated-only | Equivalent invalid payloads yield byte-stable ordered fieldErrors. |

### B. Authentication and host isolation

| ID | Level | Request / expected result |
|---|---|---|
| `STEP15-AUTH-CUSTOMER-MISSING-035` | TestServer + live-local | Protected Customer operation without JWT: exact 401 customer code. |
| `STEP15-AUTH-CUSTOMER-MALFORMED-036` | TestServer | Malformed/expired/wrong-audience Customer JWT: same 401, no token echo. |
| `STEP15-AUTH-CUSTOMER-ROLE-037` | TestServer | Authenticated wrong Customer role: exact 403 customer code. |
| `STEP15-AUTH-BUSINESS-MISSING-038` | TestServer + live-local | Protected Business operation without JWT: exact 401 business code. |
| `STEP15-AUTH-BUSINESS-MALFORMED-039` | TestServer | Malformed/expired/wrong-audience Business JWT: same 401. |
| `STEP15-AUTH-BUSINESS-ROLE-040` | TestServer | Employee/Owner/Admin policy mismatch: exact 403 business code. |
| `STEP15-AUTH-BUSINESS-ASSIGNMENT-041` | TestServer | Correct role but foreign company/branch: identical safe 403/404 contract, no enumeration. |
| `STEP15-AUTH-CROSS-CUSTOMER-TO-BUSINESS-042` | TestServer + live-local | Customer JWT on Business host: 401, never accepted as equivalent role. |
| `STEP15-AUTH-CROSS-BUSINESS-TO-CUSTOMER-043` | TestServer + live-local | Business JWT on Customer host: 401. |
| `STEP15-AUTH-DEVICE-MISSING-044` | TestServer + live-local | Device-required route missing header: exact 401 `device_token_missing`. |
| `STEP15-AUTH-DEVICE-INVALID-045` | TestServer | Malformed/unknown token: exact 401 `device_token_invalid`. |
| `STEP15-AUTH-DEVICE-EXPIRED-046` | TestServer | Expired token: exact 401 `device_token_expired`. |
| `STEP15-AUTH-DEVICE-ROTATED-047` | TestServer | Rotated old token is rejected; new token succeeds. |
| `STEP15-AUTH-DEVICE-OWNERSHIP-048` | TestServer | Foreign device gets masked feature 404 and no enumeration. |
| `STEP15-AUTH-HMAC-MISSING-049` | TestServer + live-local | Missing HMAC headers: exact English `internal_auth_missing_header` and deterministic `missingHeaders`. |
| `STEP15-AUTH-HMAC-SIGNATURE-050` | TestServer | Invalid active/next signature: exact `internal_auth_invalid_signature`. |
| `STEP15-AUTH-HMAC-TIME-051` | TestServer | Invalid/out-of-window timestamp: exact timestamp code; nonce/idempotency unconsumed. |
| `STEP15-AUTH-HMAC-NONCE-052` | TestServer | Invalid/replayed nonce: exact nonce code; no domain mutation. |
| `STEP15-AUTH-HMAC-OPERATION-053` | TestServer | Valid client without operation grant: exact `internal_service_forbidden`. |
| `STEP15-AUTH-HMAC-HTTPS-054` | Live-local | HTTP with override off: exact 403 `https_required`. |
| `STEP15-AUTH-JWT-ON-INTERNAL-055` | TestServer + live-local | Either JWT on internal route: 401 HMAC problem. |
| `STEP15-AUTH-HMAC-ON-USER-056` | TestServer | HMAC headers on user route confer no JWT authorization. |
| `STEP15-AUTH-ANONYMOUS-EXEMPTIONS-057` | Contract + TestServer | Device registration, Customer/Business auth entry points, health, OAuth callbacks as implemented, and Stripe webhook have exactly documented exemptions; no accidental global security. |

### C. Customer modern endpoint regressions

| ID | Level | Route / expected result |
|---|---|---|
| `STEP15-CUSTOMER-DEVICE-REGISTER-058` | TestServer + live-local | `POST /api/v1/devices/register`: anonymous issuance/rotation, exact validation/conflict problems, no language/auth regression. |
| `STEP15-CUSTOMER-CONFIGURATION-059` | TestServer + live-local | `GET /api/v1/configuration`: device-only, Arabic/Hebrew exact values, invalid query and unavailable problems. |
| `STEP15-CUSTOMER-CATALOG-CATEGORIES-060` | TestServer | `GET /api/v1/catalog/categories`: device-only, localized names, filters/error language. |
| `STEP15-CUSTOMER-CATALOG-BUSINESSES-061` | TestServer | `GET /api/v1/catalog/businesses`: localized list, paging/filter contract and stable failures. |
| `STEP15-CUSTOMER-CATALOG-BUSINESS-062` | TestServer | `GET /api/v1/catalog/businesses/{id}`: localized detail and masked exact not-found. |
| `STEP15-CUSTOMER-CATALOG-OFFERINGS-063` | TestServer | `GET /api/v1/catalog/businesses/{id}/offerings`: localized list/filter invariants. |
| `STEP15-CUSTOMER-CATALOG-OFFERING-064` | TestServer | `GET /api/v1/catalog/offerings/{id}`: localized selection rules/defaults and exact not-found. |
| `STEP15-CUSTOMER-DRAFT-CREATE-065` | TestServer | `POST /api/v1/checkout/drafts`: device-only; exact fieldErrors, selection validation, orderGuid/version/expiry. |
| `STEP15-CUSTOMER-DRAFT-READ-066` | TestServer | `GET /api/v1/checkout/drafts/{orderGuid}`: device ownership masking, localized snapshot, 404/410. |
| `STEP15-CUSTOMER-DRAFT-UPDATE-067` | TestServer | `PUT` same route: version/expiry/selection conflicts remain exact; no client pricing trust. |
| `STEP15-CUSTOMER-REPRICE-DIRECT-068` | TestServer | `POST /api/v1/pricing/reprice`: device-only, authoritative ordered totals/selections/payment capabilities and exact 400/413/415/503. |
| `STEP15-CUSTOMER-REPRICE-DRAFT-069` | TestServer | `POST /api/v1/checkout/reprice`: `X-Order-Guid`, expectedVersion, atomic snapshot and exact conflict/gone/failure problems. |
| `STEP15-CUSTOMER-BOOKING-CONFIRM-070` | TestServer | `POST /api/v1/bookings/from-draft`: device+User JWT, orderGuid idempotency, selection/order-insensitive reservation, immutable prices and exact 400/404/409/410/413/415/503. |
| `STEP15-CUSTOMER-PAYMENT-INTENT-071` | TestServer | `POST /api/v1/payments/intents`: device+User JWT, idempotency, Card capability, immutable booking money, exact fieldErrors/provider/state problems. |
| `STEP15-CUSTOMER-PAYMENT-READ-072` | TestServer | `GET /api/v1/payments/{id}`: device+User JWT ownership masking and client-safe response only. |
| `STEP15-CUSTOMER-INTERNAL-STATUS-073` | TestServer | `POST /api/v1/internal/bookings/status`: HMAC/idempotency, exact English callback problems and no language. |
| `STEP15-CUSTOMER-INTERNAL-RECONCILE-074` | TestServer | `POST /api/v1/internal/bookings/{reference}/reconcile`: HMAC grant/idempotency, monotonic status and stable errors. |
| `STEP15-CUSTOMER-INTERNAL-READ-075` | TestServer | `GET /api/v1/internal/bookings/{reference}`: HMAC grant, exact status schema/not-found. |
| `STEP15-CUSTOMER-INTERNAL-SHAPE-076` | TestServer | Malformed GUID/extra internal path never bypasses HMAC and returns safe 400/404. |
| `STEP15-CUSTOMER-STRIPE-WEBHOOK-077` | TestServer | `POST /api/stripe/webhook`: JWT/device exempt, exact signature/body/content errors, English/no language, dedupe/order-safe acknowledgement. |

### D. Business endpoint regressions

| ID | Level | Route family / expected result |
|---|---|---|
| `STEP15-BUSINESS-AUTH-REGISTER-078` | TestServer + live-local | `POST /api/v1/business/auth/register-owner`: anonymous, exact localized validation/identity conflict and no role escalation. |
| `STEP15-BUSINESS-AUTH-LOGIN-079` | TestServer + live-local | `POST /api/v1/business/auth/login`: anonymous, exact safe invalid-credential problem; Business token only. |
| `STEP15-BUSINESS-COMPANY-READ-080` | TestServer | `GET /api/v1/business/company`: member policy/assignment, localized profile, safe not-found. |
| `STEP15-BUSINESS-COMPANY-UPDATE-081` | TestServer | `PUT /api/v1/business/company`: Owner/Admin, exact bilingual validation and concurrency conflict. |
| `STEP15-BUSINESS-BRANCH-CREATE-082` | TestServer | `POST .../company/branches`: required Arabic/optional normalized Hebrew address fields and assignment. |
| `STEP15-BUSINESS-BRANCH-UPDATE-083` | TestServer | `PUT .../company/branches/{branchId}`: ownership, validation, not-found/conflict. |
| `STEP15-BUSINESS-CATALOG-CATEGORY-LIST-084` | TestServer | `GET .../catalog/categories`: Owner/Admin, localized representation and assignment scope. |
| `STEP15-BUSINESS-CATALOG-CATEGORY-READ-085` | TestServer | `GET .../categories/{categoryId}`: ownership-safe 404. |
| `STEP15-BUSINESS-CATALOG-CATEGORY-CREATE-086` | TestServer | `POST .../categories`: Arabic required, optional Hebrew normalized, exact fieldErrors/conflict. |
| `STEP15-BUSINESS-CATALOG-CATEGORY-UPDATE-087` | TestServer | `PUT .../categories/{categoryId}`: same plus ownership/concurrency. |
| `STEP15-BUSINESS-CATALOG-CATEGORY-DELETE-088` | TestServer | `DELETE` category: deletion/conflict/read-after-delete and no body leak. |
| `STEP15-BUSINESS-CATALOG-OFFERING-LIST-089` | TestServer | `GET .../catalog/offerings`: scoped localized list/filter contract. |
| `STEP15-BUSINESS-CATALOG-OFFERING-READ-090` | TestServer | `GET .../offerings/{offeringId}`: schemas, enums, money/duration and safe 404. |
| `STEP15-BUSINESS-CATALOG-OFFERING-CREATE-091` | TestServer | `POST .../offerings`: exact bilingual/price/duration/category fieldErrors. |
| `STEP15-BUSINESS-CATALOG-OFFERING-UPDATE-092` | TestServer | `PUT .../offerings/{offeringId}`: same plus ownership/concurrency. |
| `STEP15-BUSINESS-CATALOG-OFFERING-DELETE-093` | TestServer | `DELETE` offering: conflict and read-after-delete. |
| `STEP15-BUSINESS-CATALOG-GROUP-LIST-094` | TestServer | `GET .../offerings/{offeringId}/addon-groups`: scoped ordered selection rules. |
| `STEP15-BUSINESS-CATALOG-GROUP-READ-095` | TestServer | `GET .../addon-groups/{addonGroupId}`: exact schema/not-found. |
| `STEP15-BUSINESS-CATALOG-GROUP-CREATE-096` | TestServer | `POST .../offerings/{offeringId}/addon-groups`: selection min/max/default validation and exact fieldErrors. |
| `STEP15-BUSINESS-CATALOG-GROUP-UPDATE-097` | TestServer | `PUT .../addon-groups/{addonGroupId}`: same plus ownership/conflict. |
| `STEP15-BUSINESS-CATALOG-GROUP-DELETE-098` | TestServer | `DELETE` group: choice dependency/conflict/read-after-delete. |
| `STEP15-BUSINESS-CATALOG-CHOICE-LIST-099` | TestServer | `GET .../addon-groups/{addonGroupId}/choices`: order/localization/money/duration. |
| `STEP15-BUSINESS-CATALOG-CHOICE-READ-100` | TestServer | `GET .../addon-choices/{addonChoiceId}`: exact schema/not-found. |
| `STEP15-BUSINESS-CATALOG-CHOICE-CREATE-101` | TestServer | `POST .../addon-groups/{addonGroupId}/choices`: bilingual/default/quantity validation. |
| `STEP15-BUSINESS-CATALOG-CHOICE-UPDATE-102` | TestServer | `PUT .../addon-choices/{addonChoiceId}`: same plus ownership/conflict. |
| `STEP15-BUSINESS-CATALOG-CHOICE-DELETE-103` | TestServer | `DELETE` choice: default/rule conflict/read-after-delete. |
| `STEP15-BUSINESS-AVAILABILITY-SETTINGS-104` | TestServer | GET/PUT `.../availability/branches/{branchId}/settings`: time zone/lead/horizon/capacity exact errors. |
| `STEP15-BUSINESS-AVAILABILITY-SCHEDULES-105` | TestServer | list/read/create/update/delete recurring schedules: ownership, time/day/range overlap, read-after-delete. |
| `STEP15-BUSINESS-AVAILABILITY-OVERRIDES-106` | TestServer | list/read/create/update/delete date overrides: date/capacity/overlap and ownership. |
| `STEP15-BUSINESS-AVAILABILITY-SERVICE-AREA-107` | TestServer | GET/PUT/DELETE service area: finite/range/radius validation and deletion. |
| `STEP15-BUSINESS-WORKORDER-TRANSITION-108` | TestServer | `POST .../work-orders/{id}/transitions`: member assignment/state/version/idempotency and localized exact errors. |
| `STEP15-BUSINESS-OUTBOX-REQUEUE-109` | TestServer | `POST .../admin/booking-status-outbox/{eventId}/requeue`: Admin only, 404/409 exact problems. |
| `STEP15-BUSINESS-INTERNAL-CATALOG-110` | TestServer | `GET /api/v1/internal/catalog/snapshot`: HMAC/grant, English exact contract and version schemas. |
| `STEP15-BUSINESS-INTERNAL-VALIDATE-111` | TestServer | `POST /api/v1/internal/appointments/validate`: HMAC/idempotency, selection/pricing/slot/service-area error arrays and no localization. |
| `STEP15-BUSINESS-INTERNAL-RESERVE-112` | TestServer | `POST /api/v1/internal/reservations`: HMAC/idempotency/order-insensitive selection, authoritative price/capacity and exact English errors. |
| `STEP15-BUSINESS-INTERNAL-RESERVATION-READ-113` | TestServer | `GET .../internal/reservations/{reference}`: HMAC grant, exact schema/not-found. |

### E. Legacy, route, and transport compatibility

| ID | Level | Request / expected result |
|---|---|---|
| `STEP15-LEGACY-AUTH-114` | TestServer + contract | All `/api/Auth/*` operations retain existing anonymous/protected/OAuth behavior, are explicitly marked legacy/deprecated, and do not gain device/HMAC requirements. |
| `STEP15-LEGACY-USERS-115` | TestServer + contract | All `/api/Users*` operations retain role/self-service behavior; Step 15 errors are safe/correlated without changing DTO semantics. |
| `STEP15-LEGACY-ADDRESSES-116` | TestServer + contract | All six address operations retain JWT ownership and mutation semantics; Swagger marks legacy. |
| `STEP15-LEGACY-VEHICLES-117` | TestServer + contract | All five vehicle operations retain JWT ownership/validation; Swagger marks legacy. |
| `STEP15-LEGACY-BOOKINGS-118` | TestServer + contract | All twelve legacy booking operations retain current roles/state machine; no alias to modern immutable booking/payment flow. |
| `STEP15-LEGACY-COMPANIES-119` | TestServer + contract | All six legacy company operations retain policies and are not presented as Business API ownership. |
| `STEP15-LEGACY-SERVICES-120` | TestServer + contract | All six legacy service operations remain Customer-host-only/deprecated. |
| `STEP15-LEGACY-SERVICEOPTIONS-121` | TestServer + contract | All seven legacy option operations remain Customer-host-only/deprecated. |
| `STEP15-LEGACY-PAYMENTS-122` | TestServer + contract | Legacy payment controller operations are explicitly deprecated/hidden as frozen by Step 14; no conflict with `/api/v1/payments`. |
| `STEP15-LEGACY-HEALTH-123` | Live-local + contract | `/api/Health` and `/api/Health/db` remain anonymous, no localized business data; DB failure is safe 503. |
| `STEP15-TRANSPORT-HEAD-OPTIONS-124` | Live-local | HEAD/OPTIONS behavior is documented and never invokes a mutation. |
| `STEP15-TRANSPORT-TRAILING-SLASH-125` | Live-local | Canonical/trailing-slash behavior is deterministic and documented. |
| `STEP15-TRANSPORT-ACCEPT-126` | Live-local | Unsupported `Accept` cannot produce HTML/framework trace; documented JSON/problem behavior. |
| `STEP15-TRANSPORT-CHARSET-127` | Live-local | JSON UTF-8 accepted; unsupported charset fails safely; Arabic/Hebrew serialize valid UTF-8. |
| `STEP15-TRANSPORT-DUPLICATE-HEADERS-128` | TestServer + live-local | Duplicate auth/device/idempotency/orderGuid/HMAC headers fail closed, never first/last-wins ambiguity. |
| `STEP15-TRANSPORT-QUERY-ENCODING-129` | TestServer | Percent-encoding/casing/duplicate query behavior matches routing, language, and HMAC canonicalization. |

### F. Swagger and examples

| ID | Level | Request / expected result |
|---|---|---|
| `STEP15-SWAGGER-CUSTOMER-ROUTES-130` | Contract + live-local | Customer document contains every Customer route in coverage matrix, correct verbs, no Business routes, no duplicates. |
| `STEP15-SWAGGER-BUSINESS-ROUTES-131` | Contract + live-local | Business document contains every Business route, correct verbs, no Customer/Stripe routes. |
| `STEP15-SWAGGER-CUSTOMER-SECURITY-132` | Contract | Per-operation Customer HTTP-bearer JWT, device, combined, internal HMAC, anonymous, OAuth, and webhook security exactly match runtime. |
| `STEP15-SWAGGER-BUSINESS-SECURITY-133` | Contract | Per-operation Business HTTP-bearer JWT, assignment descriptions, internal HMAC, and anonymous auth operations exactly match runtime. |
| `STEP15-SWAGGER-NO-GLOBAL-AUTH-134` | Contract | No global security requirement accidentally locks anonymous/HMAC operations. |
| `STEP15-SWAGGER-LANGUAGE-135` | Contract | Only localized operations document optional `language` and `Accept-Language`, allowed ar/he, precedence/default, and `Content-Language`. |
| `STEP15-SWAGGER-PROBLEMS-136` | Contract | Every declared failure references reusable exact Problem Details/fieldErrors with `application/problem+json`. |
| `STEP15-SWAGGER-STATUS-CUSTOMER-137` | Contract | Customer runtime success/error statuses from Steps 8–14 are all declared, including 400/401/403/404/405/409/410/413/415/500/503 as applicable. |
| `STEP15-SWAGGER-STATUS-BUSINESS-138` | Contract | Business runtime statuses are all declared per operation; no blanket inaccurate 200-only metadata. |
| `STEP15-SWAGGER-SCHEMA-NULLABILITY-139` | Contract | Required/optional/nullability matches JSON DTO binding; blank optional Hebrew normalizes to null. |
| `STEP15-SWAGGER-SCHEMA-ENUMS-140` | Contract | String-only enum values and every catalog selection/payment/booking status value are documented; integers rejected. |
| `STEP15-SWAGGER-SCHEMA-BOUNDS-141` | Contract | IDs, string lengths, collection counts, quantity, coordinates, decimals, dates, body/header limits match validators/runtime. |
| `STEP15-SWAGGER-EXAMPLES-LOCALIZED-142` | Contract | Safe Arabic and Hebrew success/problem examples are valid against their schemas and show stable identical codes. |
| `STEP15-SWAGGER-EXAMPLES-CHECKOUT-143` | Contract | Draft/direct repricing examples show orderGuid, selections, version, authoritative item totals, discounts, grand total, currency, capabilities. |
| `STEP15-SWAGGER-EXAMPLES-BOOKING-144` | Contract | Confirmation example documents `X-Order-Guid`, immutable snapshots, idempotent replay and all relevant statuses. |
| `STEP15-SWAGGER-EXAMPLES-PAYMENT-145` | Contract | Intent request has bookingId/method only; response uses server money and client-safe confirmation; unsupported methods/capabilities documented. |
| `STEP15-SWAGGER-EXAMPLES-HMAC-146` | Contract | HMAC examples use placeholders, canonical signing explanation, timestamp/nonce/correlation/idempotency rules, no real signature. |
| `STEP15-SWAGGER-EXAMPLES-BUSINESS-147` | Contract | Required Arabic/optional Hebrew company/catalog/address examples and selection rule variants validate. |
| `STEP15-SWAGGER-IDEMPOTENCY-148` | Contract | Idempotency-Key and X-Order-Guid placement, bounds, same-body replay/different-body conflict are operation-accurate. |
| `STEP15-SWAGGER-CACHE-CORRELATION-149` | Contract | Response headers document no-store and correlation for all covered operations/problems. |
| `STEP15-SWAGGER-EXAMPLE-REDACTION-150` | Automated-only | Scan OpenAPI/examples/descriptions for secret-like values, PII, localhost production confusion, internal IDs, and implementation types. |
| `STEP15-SWAGGER-DETERMINISTIC-151` | Automated-only | Repeated generation is semantically identical, valid, reference-complete, and has unique operation IDs. |
| `STEP15-SWAGGER-ENVIRONMENT-152` | Live-local | Development/explicit test enable Swagger; production/default disable it without affecting API routes. |
| `STEP15-SWAGGER-UI-153` | Live-local | Each UI loads its own JSON under its base path and “Try it” targets only that host. |

### G. Domain invariance and adverse paths

| ID | Level | Request / expected result |
|---|---|---|
| `STEP15-INVARIANT-IDEMPOTENCY-154` | TestServer + automated-only | Language/header changes do not alter canonical hashes or replay identity for draft, booking, payment, reservation, callback. |
| `STEP15-INVARIANT-ORDERGUID-155` | TestServer | X-Order-Guid remains logical booking idempotency source; absent/mismatch/duplicate exact errors. |
| `STEP15-INVARIANT-SELECTION-156` | TestServer | Item/add-on ordering is semantically canonical; duplicates/default/min/max/quantity exact errors remain stable across languages. |
| `STEP15-INVARIANT-PRICING-157` | TestServer | Client totals/currency/discount/provider fields never override authoritative price; response ordering and decimals exact. |
| `STEP15-INVARIANT-CAPABILITIES-158` | TestServer | Payment capabilities expose Card availability/configuration and unavailable Wallet/Cash/ThirdParty consistently in pricing and intent docs/runtime. |
| `STEP15-INVARIANT-PAYMENT-159` | TestServer | Payment intent/read remain owner+device scoped, server-money-only, idempotent, and webhook-authoritative. |
| `STEP15-INVARIANT-CROSS-HOST-CORRELATION-160` | TestServer | Customer→Business call forwards safe correlation, same idempotency key, fresh HMAC nonce, and maps Business English errors to localized Customer problems without leakage. |
| `STEP15-INVARIANT-UPSTREAM-MALFORMED-161` | TestServer | Empty/invalid/wrong-schema Business response becomes exact safe Customer 503; no partial success/persistence. |
| `STEP15-INVARIANT-DB-FAILURE-162` | TestServer | Database failures on each host return exact safe correlated 503/500 mapping, preserve atomicity, and do not expose connection data. |
| `STEP15-INVARIANT-CONCURRENCY-163` | Automated-only | Localization/error wrapping does not change one-winner concurrency/idempotency/version guarantees. |
| `STEP15-INVARIANT-SERIALIZATION-164` | Contract + TestServer | JSON casing, decimal precision, UTC timestamps, string enums, null omission/presence exactly match OpenAPI examples. |

### H. Global response-header hardening

| ID | Level | Request / expected result |
|---|---|---|
| `STEP15-HEADERS-LOCALIZED-VARY-165` | TestServer + live-local | Every localized success/problem has exact `Content-Language`, preserves/merges `Vary: Accept-Language`, and remains no-store where sensitive/dynamic. |
| `STEP15-HEADERS-NONLOCALIZED-166` | TestServer | Internal HMAC, Stripe webhook, health, and Swagger omit `Content-Language`; `Vary` is absent unless another runtime concern requires it. |
| `STEP15-HEADERS-SECURITY-API-167` | TestServer + live-local | Both hosts' success, problem, empty, redirect, auth, 404, and 405 API responses carry nosniff, DENY, no-referrer, restrictive CSP and Permissions-Policy exactly once. |
| `STEP15-HEADERS-HSTS-PRODUCTION-168` | Live-local | Production-mode HTTPS on each host carries the configured HSTS value on API, Swagger-disabled 404, and error responses. |
| `STEP15-HEADERS-HSTS-DEVELOPMENT-169` | Live-local | Development/plain HTTP does not emit HSTS; insecure internal HMAC still fails closed unless its explicit local override is enabled. |
| `STEP15-HEADERS-SWAGGER-CSP-170` | Contract + live-local | Swagger UI uses only its documented narrow CSP/resources, cannot frame or gain browser capabilities, and JSON remains nosniff/no-store. |
| `STEP15-HEADERS-PIPELINE-FALLBACK-171` | TestServer | Middleware-generated auth failures, status-code fallback, model binding, exception mapping, and unknown routes all receive correlation/cache/security headers without duplicate writes. |
| `STEP15-HEADERS-HEALTH-172` | TestServer + live-local | Health and DB-health success/failure carry correlation/security and the documented cache policy, disclose no database/configuration details, and remain language-neutral. |

## 4. Route-to-behavior/test coverage matrix

`L` = language scenarios 001–019; `P` = exact problem/header/redaction
020–034; `A` = auth 035–057; `D` = domain endpoint scenario; `S` = Swagger
130–153; `I` = invariance 154–164.

| Host | Route(s) | Coverage |
|---|---|---|
| Customer | `/api/v1/devices/register` | P, A057, D058, S, I164 |
| Customer | `/api/v1/configuration` | L, P, A044–047, D059, S |
| Customer | five `/api/v1/catalog/*` reads | L, P, A044–048, D060–064, S, I164 |
| Customer | three `/api/v1/checkout/drafts*` operations | L, P, A044–048, D065–067, S, I154–157 |
| Customer | `/api/v1/pricing/reprice`, `/api/v1/checkout/reprice` | L, P, A044–048, D068–069, S, I154–158 |
| Customer | `/api/v1/bookings/from-draft` | L, P, A035–048, D070, S, I154–157,160–163 |
| Customer | `/api/v1/payments/intents`, `/api/v1/payments/{id}` | L, P, A035–048, D071–072, S, I154,158–159 |
| Customer | four `/api/v1/internal/bookings*` route shapes | P, A049–056, D073–076, S, I160–163; L017 exemption |
| Customer | `/api/stripe/webhook` | P, A057, D077, S; L018 exemption |
| Customer | `/api/Auth/*` (10), `/api/Users*` (12) | P, A035–037/057, D114–115, S |
| Customer | `/api/Addresses*` (6), `/api/Vehicles*` (5) | P, A035–037, D116–117, S |
| Customer | `/api/Bookings*` (12), `/api/Companies*` (6) | P, A035–037, D118–119, S |
| Customer | `/api/Services*` (6), `/api/ServiceOptions*` (7) | P, A035–037, D120–121, S |
| Customer | legacy `/api/Payments*`, `/api/Health*` | P, A035–037/057, D122–123, S; L019 health exemption |
| Business | two `/api/v1/business/auth/*` operations | L, P, A057, D078–079, S |
| Business | four `/api/v1/business/company*` operations | L, P, A038–043, D080–083, S |
| Business | twenty `/api/v1/business/catalog/*` operations | L, P, A038–043, D084–103, S, I164 |
| Business | fifteen `/api/v1/business/availability/*` operations | L, P, A038–043, D104–107, S |
| Business | work-order transition and admin outbox requeue | L, P, A038–043, D108–109, S, I154/163 |
| Business | four `/api/v1/internal/*` operations | P, A049–056, D110–113, S, I154/160–163; L017 exemption |
| Both | unknown/wrong verb/content/accept/charset/header/query | P022–034, E124–129, H165–172, S |
| Both | Swagger JSON/UI/environment | L019, S130–153, H166–170 |

## 5. Coverage-category disposition

| Category | Coverage / rationale |
|---|---|
| Happy path | D058–123 includes every owned route family; OpenAPI route presence is individually enumerated. |
| Malformed/model binding | P020–025, E124–129 and every mutation's D scenario. |
| Authn/authz/ownership | A035–057 plus endpoint policy/assignment assertions in D. |
| Validation boundaries | P020–024; Business catalog/availability D082–107; domain I155–159. |
| State/version/idempotency/concurrency | D065–077, D108–113, I154–163. |
| Failure/unavailability | P027–028, I160–163. |
| Deletion/read-after-delete | D088,093,098,103,105–107 and legacy regression scenarios. |
| Contract/Swagger | S130–153 plus route matrix. |
| Localization | L001–019, full exact resource oracle, each localized D scenario. |
| Security/no leakage | P029–034, A, S150, I160–163. |
| Global response headers | H165–172 covers localized/nonlocalized, API/UI, HSTS, fallback, and health response classes on both hosts. |

## 6. Code test plan

- Contract tests parse both OpenAPI documents and execute S130–153.
- Parameterized TestServer matrices execute every concrete route in §4, not one
  representative per family, for success, auth, language, malformed request,
  declared status, headers, and exact full problem strings.
- Unit snapshot tests cover every `(stable code, ar/he)` title/detail and every
  validation field code; fail on missing, duplicate, blank, fallback, or drift.
- Existing Steps 8–14 integration/relational suites remain green for domain,
  selection, pricing, booking, status, payment, webhook, HMAC, replay, and
  concurrency invariants.
- Live-local manifests are added only during implementation, then both hosts
  run against isolated databases and non-production secrets.

## 7. Completion gate

This planning task freezes **172 unique scenarios**:

- 34 cross-cutting language/problem;
- 23 authentication/isolation;
- 20 Customer modern;
- 36 Business;
- 16 legacy/transport;
- 24 Swagger;
- 11 domain/adverse invariance;
- 8 global response-header hardening.

The route inventory covers all **133 active controller operations** observed
before Step 15: 86 Customer-host operations across 19 active controllers
(`PaymentsController` is intentionally `[NonController]`) and 47 Business-host
operations across 9 controllers.

Primary levels overlap by design: 42 scenarios require or include live-local
transport evidence, 134 require or include TestServer, 35 require or include
contract assertions, and 7 require or include automated-only evidence. These
are planned levels, not execution results.

## 8. Execution results

Final gate status: **passed on 2026-08-24**.

- Static inventory: **172/172** frozen scenarios and **42/42** live mappings.
- Manifest: **90/90** live-local cases (Customer 52; Business 38).
- Harness/asset/fixture-verifier: **38/38** self-tests.
- Step 15 tests: **228/228** (Customer 135; Business 93), including
  **11/11** final invariant tests (Customer 7; Business 4).
- Full Release tests: **1,672/1,672** (Customer 1,246; Business 426).
- Clean Release builds: **2/2**, 0 warnings and 0 errors.
- Live-local execution: **90/90** across six exact Development/Production and
  HTTP/HTTPS hosts, using real opposite-host JWT variables.
- Hardened post-run database invariant gate: **1/1**.
- EF validation: both owned models current, 2/2 idempotent scripts, and 4/4
  repeated no-op updates.
- Cleanup: 0 explicit Step 15 databases, runtime artifacts, or test-port
  listeners remain.
- Genuine deferrals: **none**.

The 42 frozen live mappings are plan coverage mappings; the expanded 90-entry
manifest deliberately exercises multiple transport/environment variants for
some mappings. Sanitized aggregate evidence is committed. Raw responses,
generated credentials, database names, logs, backups, and other ignored local
runtime artifacts are not committed.

Detailed sanitized evidence is recorded in
`scripts/http-tests/plans/step-15-localization-swagger.results.md`.

Step 15 implementation is not complete until every applicable scenario is
automated and passed, exact totals/evidence are recorded, no required scenario
is merely planned/automated/failed, and any deferral meets the repository's
explicit non-shipping exception standard.
