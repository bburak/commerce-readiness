# Evaluation contract

This document fixes the definitions later milestones are scored against. Everything here is deterministic; no LLM is
ever used as a judge.

## Stores

| Store | Who reads it | Authoritative? |
|---|---|---|
| Clean | Oracle, ground-truth metrics | Yes, it is the hidden ground truth |
| Published | Future analyzer and shopper | Only per source, for that source's own domain |
| Repaired (future) | Future evaluation runner | Same rules as Published |

Within the published store:

- Pricing is authoritative for price.
- Inventory is authoritative for stock.
- Shipping rules plus the simulation clock are authoritative for delivery.
- The return policy is authoritative for returns.
- Payments is authoritative for installments.
- The catalog is authoritative for identity, attributes and description.

Catalog columns that copy another source (`listedPrice`, `stockStatus`, `deliveryPromiseIstanbul`) are projections.

## Evaluable units

Every metric is a count of passed units over applicable units. Unit IDs:

| Unit | ID | Applicable when |
|---|---|---|
| Completeness | `completeness:{sku}:{attribute}` | always (SKU x category requirement) |
| Semantic | `semantic:{sku}:{attribute}` | always (SKU x category requirement) |
| Stock | `stock:{sku}` | always |
| Price | `price:{sku}` | always |
| Delivery | `delivery:{sku}` | the catalog makes an Istanbul promise and authoritative stock exists |
| Claim | `claim:{productId}:{attribute}` | per description claim |
| Variant | `variant:{sku}` | category has variant-level attributes (Sneakers) |
| Return | `return:{category}:{question}` | per policy cell |

Each defect and decoy maps to exactly one unit (`EvaluationUnits.For`):

| Case type | Unit |
|---|---|
| `MissingRequiredAttribute` | completeness |
| `InventoryConflict` | stock |
| `PriceConflict` | price |
| `DeliveryConflict` | delivery |
| `MisleadingDescription` | claim |
| `AmbiguousReturnRule` | return |

A Milestone 1 analyzer will be scored with these counts:

- **True positive:** a flagged unit that is a defect unit.
- **False positive:** a flagged decoy unit or any other passing unit.
- **False negative:** an unflagged defect unit.

## Semantic evidence

Descriptions are assembled from templated phrases; each phrase is recorded as a `SemanticFact`
(`store`, `productId`, `attributeKey`, `assertedValue`, `phrase`, `supportsAuthoritativeValue`). A fact supports an
attribute only if `assertedValue` equals the clean-store structured value. Metrics recompute this against the clean
store rather than trusting the flag. Later AI extraction will be evaluated against these facts.

## Shopper intents

- `outcome` is one of:
  - `ValidProductsExist`: at least one SKU satisfies every mandatory constraint.
  - `NoValidProduct`: the request is complete, but no SKU satisfies it.
  - `InsufficientInformation`: the oracle needs information the text does not give, for example a shoe size or a
    destination city.
- Mandatory constraints are every non-null field of `constraints`, plus being in stock.
- `noValidAnswer` and `shouldAbstain` are flags derived from `outcome`.
- `forbidden[]` lists every SKU in the requested category that violates a constraint. Each entry gives the
  violations and `looksValidInPublished`, which is true when the published storefront alone would make the SKU look
  valid.

## Shopper output (Milestone 3) and scoring (Milestone 4)

```
ShopperResult  { intentId, recommendations[], abstained, missingInformation[] }
Recommendation { sku, claims[], uncertain }
Claim          { type, value, evidenceCallId }
EvidenceRecord { callId, tool, source, facts }
```

- **Unsupported Claim**: `evidenceCallId` is null or unknown, or the referenced `EvidenceRecord` facts contradict
  `value`.
- **Confident Wrong Recommendation**: the recommended SKU is not in `expected.validSkus` (it violates at least one
  mandatory constraint per the oracle) and `uncertain` is false.
- **Correct Abstention**: `abstained` is true with no recommendations, and the intent outcome is `NoValidProduct` or
  `InsufficientInformation`. Abstaining when valid products exist is a missed answer.
- **Trap hit**: recommending a SKU whose `forbidden` entry has `looksValidInPublished = true`, meaning the shopper was
  misled by broken published data.
