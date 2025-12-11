# Stripe Payment Integration - Complete Documentation

## Overview

This document provides comprehensive documentation for the Stripe payment integration in the Ghseeli APIs project. The integration enables secure credit card payment processing using Stripe's payment platform.

## Table of Contents

1. [Features](#features)
2. [Architecture](#architecture)
3. [Setup Guide](#setup-guide)
4. [API Documentation](#api-documentation)
5. [Webhook Configuration](#webhook-configuration)
6. [Testing](#testing)
7. [Security](#security)
8. [Troubleshooting](#troubleshooting)
9. [Production Checklist](#production-checklist)

---

## Features

### ? Implemented Features

- **Credit Card Payments**: Process payments via Stripe using tokenized payment methods
- **Automatic Payment Processing**: Payments processed immediately during booking creation
- **Refund Processing**: Full refunds via Stripe API with automatic status updates
- **Webhook Support**: Asynchronous payment confirmation via Stripe webhooks
- **Payment Tracking**: Store transaction IDs and payment intent IDs for reconciliation
- **Error Handling**: Comprehensive error handling with detailed logging
- **Status Management**: Automatic payment and booking status updates
- **Idempotent Operations**: Safe handling of duplicate webhook events

### Payment Methods Supported

| Method | Stripe Integration | Status |
|--------|-------------------|---------|
| **Credit Card** | ? Yes (via Stripe) | Completed |
| **Cash on Arrival** | ? No (manual) | Pending status |
| **Wallet** | ? No (internal) | Pending status |
| **Third Party** | ? No (future) | Not implemented |

---

## Architecture

### System Components

```
???????????????????????????????????????????????????????????????
?                      Frontend (Client)                      ?
?                     React / JavaScript                      ?
???????????????????????????????????????????????????????????????
                       ?
                       ? 1. Tokenize card with Stripe.js
                       ?    (pm_xxxxx)
                       ?
???????????????????????????????????????????????????????????????
?                  PaymentsController (API)                   ?
?                  POST /api/payments                         ?
???????????????????????????????????????????????????????????????
                       ?
                       ? 2. Validate PaymentMethodId
                       ?
                       ?
???????????????????????????????????????????????????????????????
?                    PaymentHandler                           ?
?              (Business Logic Layer)                         ?
???????????????????????????????????????????????????????????????
                       ?
                       ? 3. Process via PaymentGatewayService
                       ?
                       ?
???????????????????????????????????????????????????????????????
?                 StripePaymentService                        ?
?              (Stripe API Integration)                       ?
???????????????????????????????????????????????????????????????
                       ?
                       ? 4. Call Stripe API
                       ?
                       ?
???????????????????????????????????????????????????????????????
?                    Stripe Platform                          ?
?                  (Payment Processing)                       ?
???????????????????????????????????????????????????????????????
             ?                              ?
             ? 5a. Sync Response            ? 5b. Async Webhook
             ?                              ?
    ??????????????????          ????????????????????????
    ?  PaymentHandler?          ? StripeWebhookController?
    ?   (Update DB)  ?          ?   (Confirm Status)   ?
    ??????????????????          ????????????????????????
             ?                              ?
             ????????????????????????????????
                            ?
                  ????????????????????
                  ?    Database      ?
                  ?  (Payment + Booking)?
                  ????????????????????
```

### Data Flow

#### Payment Creation Flow
1. **Frontend**: User enters card details ? Stripe.js creates PaymentMethod token (pm_xxx)
2. **API**: Receives payment request with PaymentMethodId
3. **Validation**: Checks PaymentMethodId exists for Card payments
4. **PaymentHandler**: Validates booking ownership and status
5. **StripePaymentService**: Calls Stripe API to process payment
6. **Stripe**: Processes payment and returns result
7. **Database**: Stores payment with transaction IDs and status
8. **Booking Update**: Marks booking as paid if successful
9. **Response**: Returns payment details to frontend

#### Webhook Confirmation Flow
1. **Stripe**: Sends webhook event (payment_intent.succeeded)
2. **Signature Verification**: Validates webhook authenticity
3. **Event Processing**: Routes event to appropriate handler
4. **Status Update**: Updates payment status if needed
5. **Idempotency Check**: Skips update if already in target state
6. **Logging**: Records webhook event for audit trail
7. **Response**: Returns 200 OK to Stripe

---

## Setup Guide

### Prerequisites

- .NET 8 SDK
- Stripe account (free signup at https://stripe.com)
- Visual Studio 2022 or VS Code

### 1. Install Stripe.net Package

Already installed in the project:
```xml
<PackageReference Include="Stripe.net" Version="45.14.0" />
```

### 2. Get Stripe API Keys

#### For Test Mode:
1. Go to [Stripe Dashboard](https://dashboard.stripe.com/test/apikeys)
2. Copy your test keys:
   - **Publishable Key**: `pk_test_...` (for frontend)
   - **Secret Key**: `sk_test_...` (for backend)

#### For Production:
1. Complete Stripe account verification
2. Go to [Live Mode](https://dashboard.stripe.com/apikeys)
3. Copy your live keys:
   - **Publishable Key**: `pk_live_...`
   - **Secret Key**: `sk_live_...`

### 3. Configure User Secrets (Development)

```bash
# Navigate to project directory
cd GhseeliApis

# Initialize user secrets (already done)
dotnet user-secrets init

# Set Stripe keys
dotnet user-secrets set "Stripe:PublishableKey" "pk_test_YOUR_KEY"
dotnet user-secrets set "Stripe:SecretKey" "sk_test_YOUR_KEY"
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_YOUR_SECRET"
```

### 4. Configure Environment Variables (Production)

#### Azure App Service:
```bash
az webapp config appsettings set --name YourAppName --resource-group YourResourceGroup \
  --settings Stripe__PublishableKey="pk_live_..." \
             Stripe__SecretKey="sk_live_..." \
             Stripe__WebhookSecret="whsec_..."
```

#### Docker:
```bash
docker run -e "Stripe__SecretKey=sk_live_..." \
           -e "Stripe__PublishableKey=pk_live_..." \
           -e "Stripe__WebhookSecret=whsec_..." \
           your-image
```

### 5. Configure Webhooks

See [Webhook Configuration](#webhook-configuration) section below.

---

## API Documentation

### Base URL
- **Development**: `https://localhost:5001/api`
- **Production**: `https://yourdomain.com/api`

### Authentication
All payment endpoints require JWT authentication (except webhook endpoint).

**Header**: `Authorization: Bearer <your_jwt_token>`

---

### Create Payment

**Endpoint**: `POST /api/payments`

**Description**: Create a new payment for a booking

**Authorization**: Required (User role)

**Request Body**:
```json
{
  "bookingId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 50.00,
  "method": "Card",
  "paymentMethodId": "pm_1234567890abcdef"
}
```

**Request Fields**:
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `bookingId` | GUID | Yes | ID of the booking to pay for |
| `amount` | Decimal | Yes | Payment amount (0.01 - 999999.99) |
| `method` | Enum | Yes | Payment method: "Card", "CashOnArrival", "Wallet" |
| `paymentMethodId` | String | Conditional | Required for Card payments. Stripe payment method token from frontend |
| `transactionId` | String | No | Optional transaction ID for non-Stripe payments |

**Success Response** (201 Created):
```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "bookingId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userId": "2f5e8c9a-1234-5678-90ab-cdef12345678",
  "amount": 50.00,
  "method": "Card",
  "status": "Completed",
  "transactionId": "ch_1234567890abcdef",
  "paymentMethodId": "pm_1234567890abcdef",
  "paymentIntentId": "pi_1234567890abcdef",
  "createdAt": "2024-12-07T23:30:00Z",
  "userName": "John Doe",
  "bookingInfo": "Booking #3fa85f64"
}
```

**Error Responses**:

400 Bad Request - Missing PaymentMethodId:
```json
{
  "message": "Credit card payments require a PaymentMethodId from Stripe."
}
```

400 Bad Request - Payment Failed:
```json
{
  "message": "Payment failed: Your card was declined."
}
```

400 Bad Request - Invalid Booking:
```json
{
  "message": "Booking not found."
}
```

400 Bad Request - Payment Exists:
```json
{
  "message": "Payment already exists for this booking."
}
```

---

### Get Payment by ID

**Endpoint**: `GET /api/payments/{id}`

**Authorization**: Required

**Success Response** (200 OK):
```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "bookingId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userId": "2f5e8c9a-1234-5678-90ab-cdef12345678",
  "amount": 50.00,
  "method": "Card",
  "status": "Completed",
  "transactionId": "ch_1234567890abcdef",
  "paymentMethodId": "pm_1234567890abcdef",
  "paymentIntentId": "pi_1234567890abcdef",
  "createdAt": "2024-12-07T23:30:00Z",
  "userName": "John Doe",
  "bookingInfo": "Booking #3fa85f64"
}
```

---

### Get My Payments

**Endpoint**: `GET /api/payments/my-payments`

**Authorization**: Required

**Success Response** (200 OK):
```json
[
  {
    "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "amount": 50.00,
    "method": "Card",
    "status": "Completed",
    "createdAt": "2024-12-07T23:30:00Z"
  }
]
```

---

### Process Refund

**Endpoint**: `POST /api/payments/{id}/refund`

**Authorization**: Required (payment owner)

**Success Response** (200 OK):
```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "Refunded",
  "amount": 50.00,
  "method": "Card"
}
```

**Error Responses**:

400 Bad Request - Not Owner:
```json
{
  "message": "You can only refund your own payments."
}
```

400 Bad Request - Not Completed:
```json
{
  "message": "Only completed payments can be refunded."
}
```

400 Bad Request - Stripe Refund Failed:
```json
{
  "message": "Refund failed: Charge has already been refunded."
}
```

---

## Webhook Configuration

### Webhook Endpoint

**URL**: `POST /api/stripe/webhook`

**Authentication**: None (verified via signature)

**Purpose**: Receives asynchronous payment event notifications from Stripe

### Setup in Stripe Dashboard

1. **Go to Webhooks**
   - Navigate to: https://dashboard.stripe.com/test/webhooks
   - Click "Add endpoint"

2. **Configure Endpoint**
   - **Endpoint URL**: `https://yourdomain.com/api/stripe/webhook`
   - **Description**: "Ghseeli APIs Payment Webhook"
   - **Events to send**: Select the following:
     - `payment_intent.succeeded` - Payment completed successfully
     - `payment_intent.payment_failed` - Payment failed
     - `charge.refunded` - Refund processed
     - `payment_intent.canceled` - Payment canceled

3. **Get Webhook Secret**
   - After creating the endpoint, click "Reveal" under "Signing secret"
   - Copy the secret (starts with `whsec_`)
   - Add to User Secrets:
     ```bash
     dotnet user-secrets set "Stripe:WebhookSecret" "whsec_YOUR_SECRET"
     ```

### Events Handled

| Event Type | Description | Action |
|------------|-------------|--------|
| `payment_intent.succeeded` | Payment completed | Update status to Completed |
| `payment_intent.payment_failed` | Payment failed | Update status to Failed |
| `charge.refunded` | Refund processed | Update status to Refunded |
| `payment_intent.canceled` | Payment canceled | Update status to Failed |

### Local Testing with Stripe CLI

**Install Stripe CLI**:
```bash
# Windows (Scoop)
scoop install stripe

# macOS (Homebrew)
brew install stripe/stripe-cli/stripe

# Login
stripe login
```

**Forward Webhooks to Local Server**:
```bash
# Start API
dotnet run --project GhseeliApis/GhseeliApis.csproj

# In another terminal, forward webhooks
stripe listen --forward-to https://localhost:5001/api/stripe/webhook

# Copy the webhook signing secret and update User Secrets
```

**Trigger Test Events**:
```bash
stripe trigger payment_intent.succeeded
stripe trigger payment_intent.payment_failed
stripe trigger charge.refunded
stripe trigger payment_intent.canceled
```

---

## Testing

### Test Cards

Use these test cards in test mode:

| Card Number | Description |
|-------------|-------------|
| `4242 4242 4242 4242` | Successful payment |
| `4000 0000 0000 0002` | Card declined |
| `4000 0000 0000 9995` | Insufficient funds |
| `4000 0027 6000 3184` | 3D Secure required |

**Use any future expiration date and any 3-digit CVC**

### Unit Tests

**Total Tests**: 502 tests (461 existing + 41 Stripe tests)

**Run All Tests**:
```bash
dotnet test
```

**Run Stripe Tests Only**:
```bash
dotnet test --filter "FullyQualifiedName~Stripe"
```

**Test Coverage**:
- ? StripePaymentService: 22 tests
- ? StripeWebhookController: 19 tests
- ? PaymentHandler: Stripe integration tested
- ? PaymentsController: Payment method validation tested

---

## Security

### Security Features Implemented

1. **? Payment Method Tokenization**
   - Credit card details never touch your server
   - Stripe.js creates secure payment method tokens
   - Tokens used once for payment processing

2. **? Webhook Signature Verification**
   - HMAC SHA256 signature validation
   - Prevents webhook spoofing
   - Rejects invalid signatures with 400 Bad Request

3. **? User Authorization**
   - Users can only pay for their own bookings
   - Users can only refund their own payments
   - JWT token validation on all endpoints

4. **? Payment Validation**
   - Booking ownership verification
   - Duplicate payment prevention
   - Amount validation (0.01 - 999999.99)

5. **? Secure Configuration**
   - API keys stored in User Secrets (development)
   - Environment variables (production)
   - Never committed to source control

6. **? HTTPS Enforcement**
   - All API communication over HTTPS
   - Stripe webhook requires HTTPS

7. **? Error Handling**
   - Sanitized error messages to users
   - Detailed logging for developers
   - No sensitive data in error responses

### PCI Compliance

This integration is **PCI-compliant** because:
- ? No credit card data stored in database
- ? No credit card data touches your server
- ? Stripe handles all card processing
- ? Only payment method tokens stored

---

## Troubleshooting

### Common Issues

#### Issue: "Stripe SecretKey is not configured"

**Cause**: Stripe API key not set in configuration

**Solution**:
```bash
dotnet user-secrets set "Stripe:SecretKey" "sk_test_YOUR_KEY" --project GhseeliApis.csproj
```

---

#### Issue: "Credit card payments require a PaymentMethodId from Stripe"

**Cause**: Frontend not sending payment method token

**Solution**: Ensure frontend creates payment method first:
```javascript
const {paymentMethod} = await stripe.createPaymentMethod({
  type: 'card',
  card: cardElement
});

// Include in request
paymentMethodId: paymentMethod.id
```

---

#### Issue: "Payment failed: Your card was declined"

**Cause**: Test card declined or insufficient funds

**Solution**: 
- Use successful test card: `4242 4242 4242 4242`
- Check Stripe Dashboard for error details
- Verify amount is not too large for test mode

---

#### Issue: Webhook not receiving events

**Cause**: Endpoint not publicly accessible or incorrect URL

**Solutions**:
1. Verify endpoint URL in Stripe Dashboard
2. Check firewall allows Stripe IPs
3. Test with Stripe CLI: `stripe trigger payment_intent.succeeded`
4. Check application logs for errors

---

#### Issue: "Invalid signature" on webhook

**Cause**: Incorrect webhook secret or body parsing

**Solutions**:
1. Verify webhook secret in User Secrets matches Stripe Dashboard
2. Ensure raw request body is used (not parsed JSON)
3. Check `Stripe-Signature` header is present

---

## Production Checklist

### Before Going Live

- [ ] **Replace Test Keys with Live Keys**
  - [ ] Update `Stripe:PublishableKey` with `pk_live_...`
  - [ ] Update `Stripe:SecretKey` with `sk_live_...`
  - [ ] Use environment variables, not User Secrets

- [ ] **Configure Production Webhook**
  - [ ] Create webhook endpoint in Stripe Dashboard (live mode)
  - [ ] Use production URL: `https://yourdomain.com/api/stripe/webhook`
  - [ ] Update `Stripe:WebhookSecret` with live webhook secret
  - [ ] Select same events (payment_intent.succeeded, etc.)

- [ ] **Enable HTTPS**
  - [ ] SSL certificate installed
  - [ ] HTTPS enforced in `Program.cs`
  - [ ] Set `RequireHttpsMetadata = true` in JWT config

- [ ] **Security Review**
  - [ ] API keys stored securely (environment variables)
  - [ ] User authorization working correctly
  - [ ] Webhook signature verification enabled
  - [ ] Error messages don't expose sensitive data

- [ ] **Testing**
  - [ ] Test successful payment with small amount
  - [ ] Test declined payment
  - [ ] Test refund processing
  - [ ] Test webhook delivery
  - [ ] Verify database updates

- [ ] **Monitoring**
  - [ ] Set up logging aggregation
  - [ ] Create alerts for payment failures
  - [ ] Monitor webhook delivery success rate
  - [ ] Track payment conversion rate

- [ ] **Database Migration**
  - [ ] Run migration: `dotnet ef database update`
  - [ ] Verify `PaymentMethodId` and `PaymentIntentId` columns exist
  - [ ] Backup database before deployment

- [ ] **Documentation**
  - [ ] Update API documentation with production URLs
  - [ ] Document recovery procedures
  - [ ] Create runbook for common issues

---

## Frontend Integration Example

### React + Stripe.js

**Install Stripe**:
```bash
npm install @stripe/stripe-js @stripe/react-stripe-js
```

**Payment Form Component**:
```tsx
import React, { useState } from 'react';
import { loadStripe } from '@stripe/stripe-js';
import { CardElement, Elements, useStripe, useElements } from '@stripe/react-stripe-js';

// Initialize Stripe
const stripePromise = loadStripe('pk_test_YOUR_PUBLISHABLE_KEY');

function PaymentForm({ bookingId, amount, onSuccess }) {
  const stripe = useStripe();
  const elements = useElements();
  const [error, setError] = useState(null);
  const [processing, setProcessing] = useState(false);

  const handleSubmit = async (event) => {
    event.preventDefault();
    setProcessing(true);
    setError(null);

    if (!stripe || !elements) {
      return;
    }

    // Create payment method
    const { error: stripeError, paymentMethod } = await stripe.createPaymentMethod({
      type: 'card',
      card: elements.getElement(CardElement),
    });

    if (stripeError) {
      setError(stripeError.message);
      setProcessing(false);
      return;
    }

    // Send to backend
    try {
      const response = await fetch('/api/payments', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${yourAuthToken}`
        },
        body: JSON.stringify({
          bookingId: bookingId,
          amount: amount,
          method: 'Card',
          paymentMethodId: paymentMethod.id
        })
      });

      if (response.ok) {
        const payment = await response.json();
        onSuccess(payment);
      } else {
        const errorData = await response.json();
        setError(errorData.message || 'Payment failed');
      }
    } catch (err) {
      setError('Network error occurred');
    } finally {
      setProcessing(false);
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      <CardElement
        options={{
          style: {
            base: {
              fontSize: '16px',
              color: '#424770',
              '::placeholder': {
                color: '#aab7c4',
              },
            },
            invalid: {
              color: '#9e2146',
            },
          },
        }}
      />
      
      {error && <div style={{ color: 'red' }}>{error}</div>}
      
      <button type="submit" disabled={!stripe || processing}>
        {processing ? 'Processing...' : `Pay $${amount.toFixed(2)}`}
      </button>
    </form>
  );
}

// Usage
function App() {
  return (
    <Elements stripe={stripePromise}>
      <PaymentForm
        bookingId="your-booking-id"
        amount={50.00}
        onSuccess={(payment) => console.log('Payment successful!', payment)}
      />
    </Elements>
  );
}
```

---

## Support

### Resources

- **Stripe Documentation**: https://stripe.com/docs
- **Stripe.net SDK**: https://github.com/stripe/stripe-dotnet
- **Stripe API Reference**: https://stripe.com/docs/api
- **Stripe Dashboard**: https://dashboard.stripe.com

### Getting Help

- **Stripe Support**: Available in Stripe Dashboard
- **GitHub Issues**: Create issue in project repository
- **Email**: support@ghseeli.com (update with your contact)

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2024-12-07 | Initial Stripe integration |
|  |  | - Credit card payment processing |
|  |  | - Refund processing |
|  |  | - Webhook support |
|  |  | - 41 unit tests |

---

## License

Copyright © 2024 Ghseeli. All rights reserved.
