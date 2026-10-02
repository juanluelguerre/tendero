# Domain model

> **Generated — do not edit.** Written from the code by
> `tests/Architecture.Tests/DomainModelDocument.cs`, and a test fails when this
> file and the code disagree. Regenerate it with:
>
> ```bash
> UPDATE_DOMAIN_MODEL=1 dotnet test --project tests/Architecture.Tests
> ```

One section per bounded context, from the public types of its `*.Domain`
namespace. Types from the SharedKernel — `Money`, `LocalizedText`, `Address`
and the strongly-typed ids — appear as property types rather than as boxes,
because every context uses them and drawing them five times says nothing.
Contexts share no entities (ADR 0014), so no arrow crosses a section.

`aggregate` marks a subclass of `AggregateRoot`; `record` marks a C# record.
Events are listed rather than drawn: an aggregate raises them from its
methods, which a diagram of properties cannot show. Every event also carries
`OccurredAt`. The state machines are the aggregates' `TransitionTable`s, read
from the tables themselves (invariant 5).

## Catalog

```mermaid
classDiagram
    class AttributeDefinition {
        <<aggregate>>
        +string Code
        +LocalizedText Label
        +AttributeKind Kind
        +string? Unit
        +bool IsVariantAxis
        +bool IsFacet
        +bool IsSearchable
        +bool IsDraft
        +AttributeOption[] Options
        +string[] Aliases
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
    }
    class AttributeKind {
        <<enumeration>>
        Text
        LocalizedText
        Number
        Boolean
        Option
    }
    class AttributeOption {
        <<record>>
        +string Code
        +LocalizedText Label
        +string? Swatch
    }
    class AttributeValue {
        <<record>>
        +string Code
        +AttributeKind Kind
        +string? OptionCode
        +LocalizedText? Text
        +string? RawText
        +decimal? Number
        +bool? Flag
        +string Key
        +bool IsWorthIndexing
    }
    class Category {
        <<aggregate>>
        +string Code
        +LocalizedText Name
        +string? ParentCode
        +string Path
        +int SortOrder
        +DateTimeOffset UpdatedAt
        +string[] Ancestry
    }
    class CategoryTree {
        +Category[] All
    }
    class ExternalReference {
        <<record>>
        +string Source
        +string ExternalId
    }
    class Product {
        <<aggregate>>
        +ProductId Id
        +string Code
        +LocalizedText Name
        +LocalizedText Slug
        +LocalizedText? Description
        +string? Brand
        +DateOnly? AvailableFrom
        +string? Category
        +Money Price
        +ProductStatus Status
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +ProductImage[] Images
        +ProductImage? PrimaryImage
        +AttributeValue[] Attributes
        +ExternalReference[] ExternalReferences
        +Variant[] Variants
        +string[] VariantAxes
        +Tuple~Money, Money~ PriceRange
    }
    class ProductImage {
        <<record>>
        +ImageId Id
        +LocalizedText? Alt
        +int SortOrder
    }
    class ProductStatus {
        <<enumeration>>
        Draft
        Active
        Archived
    }
    class Variant {
        +VariantId Id
        +string Sku
        +Money Price
        +Map~string, string~ AxisValues
        +string? TaxClass
        +ImageId? Image
        +VariantStatus Status
    }
    class VariantStatus {
        <<enumeration>>
        Available
        Discontinued
    }
    AttributeDefinition "1" --> "*" AttributeOption : Options
    CategoryTree "1" --> "*" Category : All
    Product "1" --> "*" ProductImage : Images
    Product --> ProductImage : PrimaryImage
    Product "1" --> "*" AttributeValue : Attributes
    Product "1" --> "*" ExternalReference : ExternalReferences
    Product "1" --> "*" Variant : Variants
```

### Events

| Event | Carries |
|---|---|
| `AttributeDefinitionChanged` | `Code: string` |
| `CategoryChanged` | `Code: string` |
| `ProductArchived` | `ProductId: ProductId` |
| `ProductUpserted` | `ProductId: ProductId` |

## Pricing

```mermaid
classDiagram
    class AmountOffOrder {
        <<record>>
        +Money Amount
        +string Kind
        +string Canonical
    }
    class AppliedDiscount {
        <<record>>
        +string PromotionCode
        +LocalizedText Label
        +Money Amount
        +string EffectKind
        +CombinationPolicy Combination
        +DiscountOutcome Outcome
        +RuleReason? Reason
    }
    class BuyXGetY {
        <<record>>
        +int Buy
        +int Free
        +string Kind
        +string Canonical
    }
    class Campaign {
        <<aggregate>>
        +CampaignCode Code
        +LocalizedText Name
        +LocalizedText Line
        +CampaignKind Kind
        +string? CategoryCode
        +CampaignScope Scope
        +DateTimeOffset StartsAt
        +DateTimeOffset EndsAt
        +int Priority
        +string[] PromotionCodes
        +ImageId? ImageId
        +LocalizedText? ImageAlt
        +CampaignImage? Image
        +CampaignStatus Status
        +DateTimeOffset? EndedAt
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +bool IsEditable
    }
    class CampaignCode {
        <<record>>
        +string Value
    }
    class CampaignDetails {
        <<record>>
        +LocalizedText Name
        +LocalizedText Line
        +CampaignKind Kind
        +string? CategoryCode
        +DateTimeOffset StartsAt
        +DateTimeOffset EndsAt
        +int Priority
        +string[] PromotionCodes
        +CampaignImage? Image
    }
    class CampaignImage {
        <<record>>
        +ImageId Id
        +LocalizedText Alt
    }
    class CampaignKind {
        <<enumeration>>
        Event
        Season
    }
    class CampaignPhase {
        <<enumeration>>
        Upcoming
        Running
        Over
    }
    class CampaignScope {
        <<enumeration>>
        Shop
        Category
    }
    class CampaignStatus {
        <<enumeration>>
        Draft
        Scheduled
        Ended
    }
    class CartItem {
        <<record>>
        +VariantId VariantId
        +string Sku
        +int Quantity
        +Money CatalogPrice
        +string? CategoryPath
        +string? TaxClass
    }
    class CombinationPolicy {
        <<enumeration>>
        ExclusiveGlobal
        ExclusiveInGroup
        Stackable
    }
    class DiscountOutcome {
        <<enumeration>>
        Applied
        Suppressed
    }
    class FreeShipping {
        <<record>>
        +string Kind
        +string Canonical
    }
    class OfferReach {
        <<record>>
        +bool Everything
        +string[] CategoryCodes
        +string[] Skus
        +bool IsNothing
    }
    class PercentOffLine {
        <<record>>
        +decimal Percent
        +string Kind
        +string Canonical
    }
    class PriceBook {
        +PriceList[] Lists
    }
    class PriceList {
        +string Code
        +string Segment
        +string Currency
        +int Priority
        +DateTimeOffset? ValidFrom
        +DateTimeOffset? ValidTo
        +PriceListEntry[] Entries
    }
    class PriceListEntry {
        <<record>>
        +string Sku
        +Money Price
    }
    class PriceQuote {
        <<record>>
        +string QuoteId
        +string InputHash
        +DateTimeOffset IssuedAt
        +DateTimeOffset ExpiresAt
        +string Currency
        +string Segment
        +QuotedLine[] Lines
        +AppliedDiscount[] Discounts
        +TaxLineView[] Taxes
        +Money Subtotal
        +Money DiscountTotal
        +Money Shipping
        +Money TaxTotal
        +Money Total
    }
    class PricedCart {
        <<record>>
        +string Currency
        +string Segment
        +PricedLine[] Lines
        +Money Shipping
        +Money Gross
        +Money DiscountTotal
        +Money Net
    }
    class PricedLine {
        <<record>>
        +VariantId VariantId
        +string Sku
        +int Quantity
        +Money UnitPrice
        +string PriceSource
        +string? CategoryPath
        +string? TaxClass
        +Money Discount
        +Money Gross
        +Money Net
    }
    class Promotion {
        <<record>>
        +string Code
        +LocalizedText Name
        +PromotionCondition Condition
        +PromotionEffect Effect
        +CombinationPolicy Combination
        +int Priority
        +string? ExclusivityGroup
        +string? Segment
        +string? CouponCode
        +DateTimeOffset? ValidFrom
        +DateTimeOffset? ValidTo
        +bool IsForEveryone
    }
    class PromotionCondition {
        <<record>>
        +Money? MinimumSubtotal
        +string? CategoryCode
        +string? Sku
        +int? MinimumQuantity
        +string Canonical
    }
    class PromotionEffect {
        <<record>>
        <<abstract>>
        +string Kind
        +string Canonical
    }
    class QuoteValidity {
        <<enumeration>>
        Valid
        Expired
        InputsChanged
    }
    class QuotedLine {
        <<record>>
        +VariantId VariantId
        +string Sku
        +int Quantity
        +Money UnitPrice
        +string PriceSource
        +Money Discount
        +Money Net
    }
    class RuleReason {
        <<record>>
        +string Code
        +LocalizedText Explanation
    }
    class TaxLineView {
        <<record>>
        +string TaxClass
        +decimal Rate
        +Money Base
        +Money Amount
    }
    PromotionEffect <|-- AmountOffOrder
    AppliedDiscount --> RuleReason : Reason
    PromotionEffect <|-- BuyXGetY
    Campaign --> CampaignCode : Code
    Campaign --> CampaignImage : Image
    CampaignDetails --> CampaignImage : Image
    PromotionEffect <|-- FreeShipping
    PromotionEffect <|-- PercentOffLine
    PriceBook "1" --> "*" PriceList : Lists
    PriceList "1" --> "*" PriceListEntry : Entries
    PriceQuote "1" --> "*" QuotedLine : Lines
    PriceQuote "1" --> "*" AppliedDiscount : Discounts
    PriceQuote "1" --> "*" TaxLineView : Taxes
    PricedCart "1" --> "*" PricedLine : Lines
    Promotion --> PromotionCondition : Condition
    Promotion --> PromotionEffect : Effect
```

### Events

| Event | Carries |
|---|---|
| `CampaignCreated` | `Code: CampaignCode` |
| `CampaignEnded` | `Code: CampaignCode`, `From: CampaignStatus` |
| `CampaignScheduled` | `Code: CampaignCode`, `StartsAt: DateTimeOffset`, `EndsAt: DateTimeOffset` |
| `CampaignUnscheduled` | `Code: CampaignCode` |
| `CampaignUpdated` | `Code: CampaignCode` |

### Campaign states

```mermaid
stateDiagram-v2
    Draft --> Scheduled
    Draft --> Ended
    Scheduled --> Draft
    Scheduled --> Ended
```

## Inventory

```mermaid
classDiagram
    class Reservation {
        <<aggregate>>
        +Guid Id
        +OrderId OrderId
        +ReservationStatus Status
        +string? Reason
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +DateTimeOffset ExpiresAt
        +ReservationLine[] Lines
    }
    class ReservationLine {
        <<record>>
        +string Sku
        +string WarehouseCode
        +int Quantity
    }
    class ReservationStatus {
        <<enumeration>>
        Held
        Committed
        Released
        Expired
    }
    class StockItem {
        <<aggregate>>
        +string Sku
        +string WarehouseCode
        +int OnHand
        +int Reserved
        +int Available
        +DateTimeOffset UpdatedAt
    }
    class Warehouse {
        +string Code
        +LocalizedText Name
        +int Priority
        +bool IsActive
    }
    Reservation "1" --> "*" ReservationLine : Lines
```

### Events

| Event | Carries |
|---|---|
| `StockLevelChanged` | `Sku: string`, `Available: int` |
| `StockRejected` | `OrderId: OrderId`, `Reason: string` |
| `StockReserved` | `OrderId: OrderId` |

### Reservation states

```mermaid
stateDiagram-v2
    Held --> Committed
    Held --> Released
    Held --> Expired
```

## Ordering

```mermaid
classDiagram
    class Cart {
        <<aggregate>>
        +CartId Id
        +CustomerId? CustomerId
        +string Token
        +string Culture
        +string Currency
        +CartStatus Status
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +DateTimeOffset ExpiresAt
        +CartLine[] Lines
        +int ItemCount
        +bool IsEmpty
    }
    class CartLine {
        <<record>>
        +ProductId ProductId
        +VariantId VariantId
        +string Sku
        +LocalizedText ProductName
        +LocalizedText? VariantLabel
        +string? ImageId
        +int Quantity
        +string? CategoryCode
        +string? CategoryPath
    }
    class CartStatus {
        <<enumeration>>
        Open
        CheckedOut
        Abandoned
    }
    class CheckoutBuyer {
        <<record>>
        +string? FirstName
        +string? LastName
        +string? Email
        +string? Phone
        +string? FullName
    }
    class CheckoutGap {
        <<enumeration>>
        Lines
        BuyerEmail
        Destination
        ShippingOption
    }
    class CheckoutSession {
        <<aggregate>>
        +CheckoutSessionId Id
        +AgentId Owner
        +CartId CartId
        +OrderMode Mode
        +string Culture
        +CheckoutSessionStatus Status
        +CheckoutBuyer Buyer
        +Address? Destination
        +string? ShippingOptionCode
        +string[] Coupons
        +string? ShownQuoteHash
        +OrderId? OrderId
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +DateTimeOffset ExpiresAt
    }
    class CheckoutSessionStatus {
        <<enumeration>>
        Open
        Completed
        Canceled
    }
    class Order {
        <<aggregate>>
        +OrderId Id
        +CustomerId CustomerId
        +string IdempotencyKey
        +string Culture
        +string Currency
        +OrderStatus Status
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +Address ShippingAddress
        +Address BillingAddress
        +OrderShipping Shipping
        +OrderQuote Quote
        +OrderTotals Totals
        +OrderMode Mode
        +string? ContactEmail
        +OrderMandate? Mandate
        +AgentId? PlacedBy
        +OrderChannel? Channel
        +DateTimeOffset? ConfirmedAt
        +DateTimeOffset? ShippedAt
        +OrderDiscount[] Discounts
        +OrderTax[] Taxes
        +OrderPayment? Payment
        +OrderStop? Stop
        +OrderLine[] Lines
        +Money LinesTotal
        +Money Total
    }
    class OrderChannel {
        <<enumeration>>
        Web
        Mcp
        Ucp
    }
    class OrderDiscount {
        <<record>>
        +string PromotionCode
        +string Label
        +Money Amount
    }
    class OrderLine {
        <<record>>
        +ProductId ProductId
        +VariantId VariantId
        +string Sku
        +string ProductName
        +string? VariantLabel
        +string? ImageId
        +Money UnitPrice
        +int Quantity
        +string? CategoryCode
        +string? CategoryPath
        +Money Total
    }
    class OrderMandate {
        <<record>>
        +string Kind
        +string Issuer
        +string Reference
        +decimal ApprovedTotal
        +string Currency
        +DateTimeOffset? ExpiresAt
    }
    class OrderMode {
        <<enumeration>>
        Live
        Test
    }
    class OrderPayment {
        <<record>>
        +string Provider
        +string AuthorizationId
        +string? CaptureId
    }
    class OrderQuote {
        <<record>>
        +string QuoteId
        +string InputHash
        +DateTimeOffset IssuedAt
    }
    class OrderShipping {
        <<record>>
        +string OptionCode
        +string Label
        +Money Amount
        +int? EstimatedDays
    }
    class OrderStatus {
        <<enumeration>>
        Pending
        PaymentAuthorized
        PaymentFailed
        Confirmed
        Shipped
        Delivered
        Cancelled
    }
    class OrderStop {
        <<record>>
        +string Code
        +string? Detail
    }
    class OrderTax {
        <<record>>
        +string TaxClass
        +decimal Rate
        +Money Base
        +Money Amount
    }
    class OrderTotals {
        <<record>>
        +Money Subtotal
        +Money DiscountTotal
        +Money Shipping
        +Money TaxTotal
        +Money Total
    }
    class ReturnLine {
        <<record>>
        +VariantId VariantId
        +string Sku
        +int Quantity
        +ReturnReason Reason
        +string? Comment
    }
    class ReturnReason {
        <<enumeration>>
        WrongSize
        NotAsDescribed
        Damaged
        WrongItem
        ChangedMind
    }
    class ReturnRequest {
        <<aggregate>>
        +ReturnRequestId Id
        +OrderId OrderId
        +int Number
        +CustomerId CustomerId
        +ReturnStatus Status
        +string? Resolution
        +Money? RefundAmount
        +string? RefundReference
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +ReturnLine[] Lines
    }
    class ReturnStatus {
        <<enumeration>>
        Requested
        Approved
        Received
        Refunded
        Rejected
        Cancelled
    }
    Cart "1" --> "*" CartLine : Lines
    CheckoutSession --> CheckoutBuyer : Buyer
    Order --> OrderShipping : Shipping
    Order --> OrderQuote : Quote
    Order --> OrderTotals : Totals
    Order --> OrderMandate : Mandate
    Order "1" --> "*" OrderDiscount : Discounts
    Order "1" --> "*" OrderTax : Taxes
    Order --> OrderPayment : Payment
    Order --> OrderStop : Stop
    Order "1" --> "*" OrderLine : Lines
    ReturnRequest "1" --> "*" ReturnLine : Lines
```

### Events

| Event | Carries |
|---|---|
| `CheckoutSessionCanceled` | `SessionId: CheckoutSessionId` |
| `CheckoutSessionCompleted` | `SessionId: CheckoutSessionId`, `OrderId: OrderId` |
| `OrderCancelled` | `OrderId: OrderId`, `Reason: string` |
| `OrderConfirmed` | `OrderId: OrderId` |
| `OrderDelivered` | `OrderId: OrderId` |
| `OrderPaymentAuthorized` | `OrderId: OrderId` |
| `OrderPaymentCaptured` | `OrderId: OrderId`, `Provider: string`, `CaptureId: string` |
| `OrderPaymentFailed` | `OrderId: OrderId`, `Reason: string` |
| `OrderPlaced` | `OrderId: OrderId` |
| `OrderShipped` | `OrderId: OrderId` |
| `ReturnApproved` | `ReturnId: ReturnRequestId`, `OrderId: OrderId` |
| `ReturnReceived` | `ReturnId: ReturnRequestId`, `OrderId: OrderId` |
| `ReturnRefunded` | `ReturnId: ReturnRequestId`, `OrderId: OrderId`, `Amount: Money` |
| `ReturnRejected` | `ReturnId: ReturnRequestId`, `OrderId: OrderId`, `Reason: string` |
| `ReturnRequested` | `ReturnId: ReturnRequestId`, `OrderId: OrderId` |

### Cart states

```mermaid
stateDiagram-v2
    Open --> CheckedOut
    Open --> Abandoned
```

### CheckoutSession states

```mermaid
stateDiagram-v2
    Open --> Completed
    Open --> Canceled
```

### Order states

```mermaid
stateDiagram-v2
    Pending --> PaymentAuthorized
    Pending --> PaymentFailed
    Pending --> Cancelled
    PaymentAuthorized --> Confirmed
    PaymentAuthorized --> Cancelled
    PaymentFailed --> Pending
    PaymentFailed --> Cancelled
    Confirmed --> Shipped
    Confirmed --> Cancelled
    Shipped --> Delivered
```

### ReturnRequest states

```mermaid
stateDiagram-v2
    Requested --> Approved
    Requested --> Rejected
    Requested --> Cancelled
    Approved --> Received
    Approved --> Cancelled
    Received --> Refunded
    Received --> Rejected
```

## Accounts

```mermaid
classDiagram
    class Agent {
        <<aggregate>>
        +AgentId Id
        +string DisplayName
        +string Operator
        +AgentTrust Trust
        +AgentStatus Status
        +string? SuspensionReason
        +DateTimeOffset RegisteredAt
        +DateTimeOffset UpdatedAt
        +AgentStanding Standing
    }
    class AgentStatus {
        <<enumeration>>
        Active
        Suspended
    }
    class BrowsingHistory {
        <<aggregate>>
        +CustomerId CustomerId
        +bool IsEnabled
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
    }
    class BrowsingKind {
        <<enumeration>>
        View
        Search
    }
    class Customer {
        <<aggregate>>
        +CustomerId Id
        +string? Subject
        +string? DisplayName
        +string Culture
        +string Segment
        +CustomerId? SupersededBy
        +DateTimeOffset CreatedAt
        +DateTimeOffset UpdatedAt
        +bool IsGuest
    }
    class TrailEntry {
        <<record>>
        +string Key
        +DateTimeOffset At
    }
```

### Events

| Event | Carries |
|---|---|
| `AgentRegistered` | `AgentId: AgentId`, `Trust: AgentTrust` |
| `AgentReinstated` | `AgentId: AgentId` |
| `AgentSuspended` | `AgentId: AgentId`, `Reason: string` |
| `AgentTrustChanged` | `AgentId: AgentId`, `From: AgentTrust`, `To: AgentTrust` |
| `CustomerIdentified` | `CustomerId: CustomerId`, `Subject: string` |
| `CustomerRegistered` | `CustomerId: CustomerId` |

### Agent states

```mermaid
stateDiagram-v2
    Active --> Suspended
    Suspended --> Active
```

## Knowledge

```mermaid
classDiagram
    class ClaimId {
        <<record>>
        +Guid Value
    }
    class ClaimStatus {
        <<enumeration>>
        Proposed
        Approved
        Rejected
        Superseded
    }
    class Evidence {
        <<record>>
        +SourceDocumentId Document
        +Quote Quote
        +double Support
    }
    class ProductClaim {
        <<aggregate>>
        +ClaimId Id
        +ProductId ProductId
        +LocalizedText Statement
        +string? AttributeCode
        +string? Value
        +Provenance Provenance
        +ClaimStatus Status
        +string? ReviewedBy
        +DateTimeOffset? ReviewedAt
        +DateTimeOffset CreatedAt
        +Evidence[] Evidence
        +string? AppliedAs
        +string? ApplicationRefused
        +double Support
        +bool IsWeaklySupported
    }
    class Provenance {
        <<record>>
        +string Extractor
        +string? ModelId
        +string? ModelRevision
        +string? PromptVersion
        +string RunId
        +DateTimeOffset At
    }
    class Quote {
        <<record>>
        +string Text
        +int Start
        +int End
    }
    class SourceDocument {
        +SourceDocumentId Id
        +SourceKind Kind
        +ProductId ProductId
        +string Culture
        +string Text
        +DateTimeOffset RetrievedAt
        +DateTimeOffset? ProposedAt
        +int FailedAttempts
        +bool IsWaiting
        +string? LastFailure
    }
    class SourceDocumentId {
        <<record>>
        +string Value
    }
    class SourceKind {
        <<enumeration>>
        ProductDescription
    }
    Evidence --> SourceDocumentId : Document
    Evidence --> Quote : Quote
    ProductClaim --> ClaimId : Id
    ProductClaim --> Provenance : Provenance
    ProductClaim "1" --> "*" Evidence : Evidence
    SourceDocument --> SourceDocumentId : Id
```

### Events

| Event | Carries |
|---|---|
| `ClaimApproved` | `ClaimId: ClaimId`, `ProductId: ProductId`, `AttributeCode: string?`, `Value: string?`, `ApprovedBy: string` |
| `ClaimProposed` | `ClaimId: ClaimId`, `ProductId: ProductId` |
| `ClaimRejected` | `ClaimId: ClaimId`, `ProductId: ProductId`, `RejectedBy: string` |
| `ClaimSuperseded` | `ClaimId: ClaimId`, `ProductId: ProductId`, `WasApproved: bool` |

### ProductClaim states

```mermaid
stateDiagram-v2
    Proposed --> Approved
    Proposed --> Rejected
    Proposed --> Superseded
    Approved --> Superseded
```

## Assist

```mermaid
classDiagram
    class ActionProposal {
        <<aggregate>>
        +ProposalId Id
        +string Command
        +string Payload
        +string Subject
        +string? Before
        +string Reason
        +ProposalProvenance Provenance
        +DateTimeOffset ProposedAt
        +ProposalStatus Status
        +string? DecidedBy
        +DateTimeOffset? DecidedAt
        +string? Result
        +UInt32 Version
    }
    class ProposalProvenance {
        <<record>>
        +string? Profile
        +string? Model
        +string? PromptVersion
        +string? ConversationId
        +string? TraceId
        +string AskedBy
    }
    class ProposalStatus {
        <<enumeration>>
        Proposed
        Approved
        Applied
        Failed
        Rejected
        Stale
    }
    ActionProposal --> ProposalProvenance : Provenance
```

### ActionProposal states

```mermaid
stateDiagram-v2
    Proposed --> Approved
    Proposed --> Rejected
    Proposed --> Stale
    Approved --> Applied
    Approved --> Failed
```
