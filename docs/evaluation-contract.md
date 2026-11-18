# Evaluation contract

This document fixes the definitions later milestones are scored against. Everything here is deterministic; no LLM is
ever used as a judge.

## Stores

| Store | Who reads it | Authoritative? |
|---|---|---|
| Clean | Oracle, ground-truth metrics | Yes, it is the hidden ground truth |
| Published | Readiness analyzer, future shopper | Only per source, for that source's own domain |
| Repaired (future) | Future evaluation runner | Same rules as Published |

Within the published store:

- Pricing is authoritative for price.
- Inventory is authoritative for stock.
- Shipping rules plus the simulation clock are authoritative for delivery.
- The return policy is authoritative for returns.
- Payments is authoritative for installments.
- The catalog is authoritative for identity, attributes and description.

Catalog columns that copy another source (`listedPrice`, `stockStatus`, `deliveryPromiseIstanbul`) are projections.

Each store folder carries a `store.json` manifest: `schemaVersion`, `storeVersion`, `kind` and `asOf`. The current
schema version is 1, and readers reject any other version. `asOf` is the simulation clock the folder's
time-sensitive data refers to, so an observable store can be analyzed without reading `eval/`.

## Evaluable units

Every metric is a count of passed units over applicable units. The applicable set (`EvaluationUnitSet`) is built once
from the clean store and evaluated against every snapshot, so clean, published and repaired stores share
denominators. Each unit is `Passed`, `Failed` or `NotEvaluable`. `NotEvaluable` means the observed snapshot lacks the
data the unit needs; it stays in the denominator and counts as not passing.

Unit IDs:

| Unit | ID | Applicable when (decided by the clean store) |
|---|---|---|
| Completeness | `completeness:{sku}:{attribute}` | always (SKU x category requirement) |
| Semantic | `semantic:{sku}:{attribute}` | always (SKU x category requirement) |
| Stock | `stock:{sku}` | always |
| Price | `price:{sku}` | always |
| Delivery | `delivery:{sku}` | the SKU has authoritative stock, so a delivery date exists; no published promise is `NotEvaluable` |
| Claim | `claim:{productId}:{attribute}` | per product x describable attribute (product-level, except brand); no claim passes |
| Variant claim | `claim:{sku}:{attribute}` | per SKU x variant-level attribute (Sneakers: shoeSize, color); only SKU-scoped facts of that SKU count |
| Variant | `variant:{sku}` | category has variant-level attributes (Sneakers) |
| Return | `return:{category}:{question}` | per policy cell |

Defect-to-unit traceability (`InjectedDefect.expectedImpacts`, computed by `EvaluationUnits.ExpectedImpacts`). The
first unit listed is the primary one:

| Defect type | Expected impacts |
|---|---|
| `MissingRequiredAttribute` | completeness; plus semantic when no supporting text evidence exists |
| `InventoryConflict` | stock; plus delivery (`NotEvaluable`) when the stale row says `OUT_OF_STOCK` |
| `PriceConflict` | price |
| `DeliveryConflict` | delivery |
| `MisleadingDescription` | claim |
| `AmbiguousReturnRule` | return |

A decoy maps to the single unit it nearly breaks (`EvaluationUnits.For`), and that unit must stay passing. Unique
defects and metric failures are reported separately in `metrics.json` (`publishedDefectCoverage`).

A Milestone 1 analyzer will be scored with these counts:

- **True positive:** a flagged unit that is a defect unit.
- **False positive:** a flagged decoy unit or any other passing unit.
- **False negative:** an unflagged defect unit.

## Semantic evidence

Descriptions are assembled from templated phrases; each phrase is recorded as a `SemanticFact`
(`store`, `productId`, `sku`, `attributeKey`, `assertedValue`, `phrase`, `supportsAuthoritativeValue`). A fact
supports an attribute only if `assertedValue` equals the clean-store structured value at the fact's scope. Metrics
recompute this against the clean store rather than trusting the flag. Later AI extraction will be evaluated against
these facts. Scope rules (`SemanticSupport.AppliesTo` and `SemanticSupport.Supports`):

| Fact | Evidences | Claim unit |
|---|---|---|
| Product-scoped (`sku` null), product-level attribute | that attribute for every SKU of the product | `claim:{productId}:{attribute}` |
| SKU-scoped, variant-level attribute | that attribute for that SKU only | `claim:{sku}:{attribute}` |
| SKU-scoped, product-level attribute | that attribute for that SKU only | `claim:{productId}:{attribute}` |
| Product-scoped, variant-level attribute (unresolved) | nothing | none |

An extractor that reads "43 numara siyah seçenek mevcut." must resolve the product's variant with `shoeSize=43` and
`color=Siyah` and emit two SKU-scoped facts for it. If no single variant matches, the claim stays unresolved, never
counts as evidence and is not attributed to any unit. Phantom variant claims (a size or color the product does not
have) are not a Milestone 0 defect type.

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

## Analyzer evaluation (Milestone 1)

The readiness analyzer reads only the published store and its manifest. `AnalyzerEvaluator` is the only place where
analyzer findings meet ground truth.

**Matching.** Findings, injected defects and decoys are reduced to `CaseKey(type, target, field)`. The finding type
maps one-to-one onto a defect type, and the target is chosen by type:

| Type | Target |
|---|---|
| `AmbiguousReturnRule` | policy cell `{category}:{question}` |
| `MissingRequiredAttribute` | SKU for variant-level keys (`shoeSize`, `color`), product otherwise |
| `InventoryConflict`, `PriceConflict`, `DeliveryConflict` | SKU |
| `MisleadingDescription` (defects only) | product |

The `field` value uses the published field names (`stockStatus`, `listedPrice`, `deliveryPromiseIstanbul`, `answer`)
or the attribute key. Summary text and IDs never take part in matching.

Matching is one-to-one:

- Findings are processed in ordinal `findingId` order.
- The first finding with a defect's key matches that defect.
- Further findings with the same key are false positives, reported with `duplicateOfDefectId`.
- A finding whose key equals a decoy's key is a false positive that hits that decoy.

**Rates** (null when the denominator is 0):

- Precision = true-positive findings / findings
- Deterministic recall = detected defects of supported types / injected defects of supported types
- Overall recall = detected defects / all injected defects
- Decoy false-positive rate = decoys flagged by at least one finding / decoys

Supported types come from `ReadinessAnalyzer.SupportedFindingTypes`. `MisleadingDescription` is not supported by the
deterministic baseline; its defects are reported as `deferredDefects`, not as false negatives.

## Retrieval benchmark (Milestone 2)

`eval/retrieval-benchmark.json` holds the generated retrieval cases (shopper-derived and discovery-only), their hard
filters, hidden relevance constraints and oracle-computed relevant SKUs. Its relevance definition, metrics and
provenance rules are specified in [retrieval-contract.md](retrieval-contract.md).

## Shopper output and scoring (Milestone 3)

The authoritative shopper contract and metrics live in [shopper-contract.md](shopper-contract.md). Summary:

```
ShopperResult  { schemaVersion, outcome, clarification?, recommendations[], noMatchReason?, supportingEvidenceCallIds[] }
Recommendation { sku, confidence, claims[] }
GroundedClaim  { type, attributeField?/city?/returnQuestion?, value, evidenceCallIds[] }
ToolInvocationTrace { evidenceCallId, toolName, arguments, ok, data?, ... }
```

- **Unsupported Claim**: missing, unknown, wrong-source, failed, wrong-entity or contradicted evidence relative to the
  claim-evidence matrix.
- **Confident Wrong Recommendation**: a Confirmed SKU not in `expected.validSkus`.
- **Grounded recommendation**: clean-valid primary SKU, every mandatory constraint grounded by tool evidence, no
  contradicted claim.
- **Correct NoMatch / Clarification**: mapped from `NoValidProduct` / `InsufficientInformation`. Clarification must
  include every expected typed missing field. Grounded NoMatch is a separate diagnostic of decisive tool evidence.
- **Trap hit**: recommending a SKU whose `forbidden` entry has `looksValidInPublished = true`.
