# VIIJAR Tenant Mobile App - API Documentation

## Base URL
```
http://localhost:5032/api/tenant
```

## Overview
This API provides complete functionality for the VIIJAR Tenant Mobile Application, allowing tenants to manage their rental accounts, view leases, make payments, and submit maintenance requests.

---

## Table of Contents
1. [Authentication](#authentication)
2. [Profile Management](#profile-management)
3. [Lease Management](#lease-management)
4. [Payment Management](#payment-management)
5. [Maintenance Requests](#maintenance-requests)
6. [Dashboard](#dashboard)
7. [Notifications](#notifications)
8. [Error Handling](#error-handling)
9. [Data Models](#data-models)

---

## Authentication

### 1. Login
**Endpoint:** `POST /api/tenant/login`

**Description:** Authenticate a tenant using their Civil ID (PACI Number) and password.

**Request Body:**
```json
{
  "paciNumber": "29012345678",
  "password": "Welcome@123"
}
```

**Success Response (200 OK):**
```json
{
  "success": true,
  "message": "Login successful",
  "data": {
    "tenantId": 1,
    "firstName": "Ahmed",
    "lastName": "Al-Mansouri",
    "email": "ahmed@example.com",
    "phoneNumber": "+96512345678",
    "paciNumber": "29012345678",
    "status": "Active"
  }
}
```

**Error Response (401 Unauthorized):**
```json
{
  "success": false,
  "message": "Invalid Civil ID or password"
}
```

---

### 2. Change Password
**Endpoint:** `POST /api/tenant/change-password`

**Description:** Change the tenant's password.

**Request Body:**
```json
{
  "tenantId": 1,
  "oldPassword": "Welcome@123",
  "newPassword": "NewSecure@456"
}
```

**Success Response (200 OK):**
```json
{
  "success": true,
  "message": "Password changed successfully"
}
```

**Error Response (400 Bad Request):**
```json
{
  "success": false,
  "message": "Current password is incorrect"
}
```

---

## Profile Management

### 3. Get Profile
**Endpoint:** `GET /api/tenant/{tenantId}/profile`

**Description:** Retrieve the tenant's profile information.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Success Response (200 OK):**
```json
{
  "tenantId": 1,
  "firstName": "Ahmed",
  "lastName": "Al-Mansouri",
  "email": "ahmed@example.com",
  "phoneNumber": "+96512345678",
  "paciNumber": "29012345678",
  "governmentId": "29012345678",
  "dateOfBirth": "1990-01-15T00:00:00Z",
  "nationality": "Kuwaiti",
  "status": "Active",
  "company": {
    "companyId": 1,
    "companyName": "ABC Corporation"
  }
}
```

**Error Response (404 Not Found):**
```json
{
  "success": false,
  "message": "Tenant not found"
}
```

---

### 4. Update Profile
**Endpoint:** `PUT /api/tenant/{tenantId}/profile`

**Description:** Update tenant's contact information.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Request Body:**
```json
{
  "email": "newemail@example.com",
  "phoneNumber": "+96598765432"
}
```

**Success Response (200 OK):**
```json
{
  "success": true,
  "message": "Profile updated successfully",
  "data": {
    "tenantId": 1,
    "email": "newemail@example.com",
    "phoneNumber": "+96598765432"
  }
}
```

---

## Lease Management

### 5. Get All Leases
**Endpoint:** `GET /api/tenant/{tenantId}/leases`

**Description:** Retrieve all active leases for the tenant.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": [
    {
      "leaseId": 1,
      "propertyName": "Marina Tower",
      "unitNumber": "A-101",
      "leaseStartDate": "2024-01-01T00:00:00Z",
      "leaseEndDate": "2024-12-31T00:00:00Z",
      "monthlyRent": 500.00,
      "securityDeposit": 1000.00,
      "status": "Active",
      "daysRemaining": 245
    }
  ]
}
```

---

### 6. Get Lease Details
**Endpoint:** `GET /api/tenant/{tenantId}/leases/{leaseId}`

**Description:** Retrieve detailed information about a specific lease.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID
- `leaseId` (integer, required): The lease ID

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "leaseId": 1,
    "unit": {
      "unitId": 1,
      "unitNumber": "A-101",
      "bedrooms": 2,
      "bathrooms": 2,
      "squareFeet": 1200,
      "property": {
        "propertyId": 1,
        "propertyName": "Marina Tower",
        "address": "Salmiya, Block 5",
        "city": "Salmiya"
      }
    },
    "leaseStartDate": "2024-01-01T00:00:00Z",
    "leaseEndDate": "2024-12-31T00:00:00Z",
    "monthlyRent": 500.00,
    "securityDeposit": 1000.00,
    "paymentDueDay": 1,
    "status": "Active",
    "contractTerms": "Standard rental agreement..."
  }
}
```

**Error Response (404 Not Found):**
```json
{
  "success": false,
  "message": "Lease not found or unauthorized"
}
```

---

## Payment Management

### 7. Get Payment History
**Endpoint:** `GET /api/tenant/{tenantId}/payments`

**Description:** Retrieve payment history with pagination support.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Query Parameters:**
- `pageNumber` (integer, optional, default: 1): Page number
- `pageSize` (integer, optional, default: 20): Number of records per page

**Example:** `GET /api/tenant/1/payments?pageNumber=1&pageSize=10`

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "payments": [
      {
        "paymentId": 1,
        "leaseId": 1,
        "propertyName": "Marina Tower",
        "unitNumber": "A-101",
        "amountPaid": 500.00,
        "paymentDate": "2024-11-01T10:30:00Z",
        "paymentMethod": "Bank Transfer",
        "transactionReference": "TXN123456789",
        "paymentStatus": "Completed"
      }
    ],
    "pageNumber": 1,
    "pageSize": 10,
    "totalRecords": 25,
    "totalPages": 3
  }
}
```

---

### 8. Get Payment Details
**Endpoint:** `GET /api/tenant/{tenantId}/payments/{paymentId}`

**Description:** Retrieve details of a specific payment.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID
- `paymentId` (integer, required): The payment ID

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "paymentId": 1,
    "lease": {
      "leaseId": 1,
      "monthlyRent": 500.00,
      "unit": {
        "unitNumber": "A-101",
        "property": {
          "propertyName": "Marina Tower",
          "address": "Salmiya, Block 5"
        }
      }
    },
    "amountPaid": 500.00,
    "paymentDate": "2024-11-01T10:30:00Z",
    "paymentMethod": "Bank Transfer",
    "transactionReference": "TXN123456789",
    "paymentStatus": "Completed",
    "notes": "October 2024 rent payment"
  }
}
```

---

### 9. Submit Payment
**Endpoint:** `POST /api/tenant/{tenantId}/payments`

**Description:** Submit a new payment for review by property management.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Request Body:**
```json
{
  "leaseId": 1,
  "amount": 500.00,
  "paymentMethod": "Bank Transfer",
  "transactionReference": "TXN123456789",
  "notes": "November 2024 rent payment"
}
```

**Payment Methods:** `"Bank Transfer"`, `"Credit Card"`, `"Cash"`, `"Cheque"`, `"Online Payment"`

**Success Response (201 Created):**
```json
{
  "success": true,
  "message": "Payment submitted successfully. It will be verified by property management.",
  "data": {
    "paymentId": 26,
    "leaseId": 1,
    "amountPaid": 500.00,
    "paymentDate": "2024-11-10T14:30:00Z",
    "paymentStatus": "Pending"
  }
}
```

**Error Response (400 Bad Request):**
```json
{
  "success": false,
  "message": "Validation error: Amount must be greater than 0"
}
```

---

## Maintenance Requests

### 10. Get Maintenance Requests
**Endpoint:** `GET /api/tenant/{tenantId}/maintenance-requests`

**Description:** Retrieve all maintenance requests for the tenant.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Query Parameters:**
- `status` (string, optional): Filter by status (`"Open"`, `"In Progress"`, `"Completed"`, `"Cancelled"`)

**Example:** `GET /api/tenant/1/maintenance-requests?status=Open`

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": [
    {
      "requestId": 1,
      "propertyName": "Marina Tower",
      "unitNumber": "A-101",
      "category": "Plumbing",
      "priority": "High",
      "description": "Leaking faucet in kitchen",
      "status": "In Progress",
      "requestDate": "2024-11-05T09:00:00Z",
      "assignedToUser": "John Maintenance",
      "estimatedCost": 50.00
    }
  ]
}
```

---

### 11. Get Maintenance Request Details
**Endpoint:** `GET /api/tenant/{tenantId}/maintenance-requests/{requestId}`

**Description:** Retrieve details of a specific maintenance request.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID
- `requestId` (integer, required): The request ID

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "requestId": 1,
    "property": {
      "propertyId": 1,
      "propertyName": "Marina Tower",
      "address": "Salmiya, Block 5"
    },
    "unit": {
      "unitId": 1,
      "unitNumber": "A-101"
    },
    "category": "Plumbing",
    "priority": "High",
    "description": "Leaking faucet in kitchen. Water dripping constantly.",
    "status": "In Progress",
    "requestDate": "2024-11-05T09:00:00Z",
    "completedDate": null,
    "assignedToUser": {
      "userId": "user123",
      "fullName": "John Maintenance"
    },
    "estimatedCost": 50.00,
    "actualCost": null,
    "notes": "Scheduled for tomorrow morning"
  }
}
```

---

### 12. Create Maintenance Request
**Endpoint:** `POST /api/tenant/{tenantId}/maintenance-requests`

**Description:** Submit a new maintenance request.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Request Body:**
```json
{
  "leaseId": 1,
  "category": "Plumbing",
  "priority": "High",
  "description": "Leaking faucet in kitchen. Water dripping constantly."
}
```

**Categories:** `"Plumbing"`, `"Electrical"`, `"HVAC"`, `"Appliance"`, `"Structural"`, `"Other"`

**Priorities:** `"Low"`, `"Medium"`, `"High"`, `"Emergency"`

**Success Response (201 Created):**
```json
{
  "success": true,
  "message": "Maintenance request submitted successfully",
  "data": {
    "requestId": 15,
    "propertyId": 1,
    "unitId": 1,
    "category": "Plumbing",
    "priority": "High",
    "status": "Open",
    "requestDate": "2024-11-10T14:45:00Z"
  }
}
```

**Error Response (400 Bad Request):**
```json
{
  "success": false,
  "message": "Validation error: Description is required"
}
```

---

## Dashboard

### 13. Get Dashboard
**Endpoint:** `GET /api/tenant/{tenantId}/dashboard`

**Description:** Retrieve comprehensive dashboard data including tenant info, statistics, recent activity, and summaries.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "tenant": {
      "tenantId": 1,
      "firstName": "Ahmed",
      "lastName": "Al-Mansouri",
      "email": "ahmed@example.com",
      "phoneNumber": "+96512345678"
    },
    "statistics": {
      "activeLeases": 1,
      "totalPaymentsThisYear": 5500.00,
      "pendingMaintenanceRequests": 2,
      "nextPaymentDue": "2024-12-01T00:00:00Z",
      "nextPaymentAmount": 500.00
    },
    "activeLeases": [
      {
        "leaseId": 1,
        "propertyName": "Marina Tower",
        "unitNumber": "A-101",
        "monthlyRent": 500.00,
        "leaseEndDate": "2024-12-31T00:00:00Z",
        "status": "Active"
      }
    ],
    "recentPayments": [
      {
        "paymentId": 25,
        "amountPaid": 500.00,
        "paymentDate": "2024-11-01T10:30:00Z",
        "paymentStatus": "Completed",
        "propertyName": "Marina Tower"
      }
    ],
    "pendingMaintenanceRequests": [
      {
        "requestId": 14,
        "category": "Plumbing",
        "priority": "High",
        "status": "In Progress",
        "requestDate": "2024-11-05T09:00:00Z",
        "propertyName": "Marina Tower"
      }
    ]
  }
}
```

---

## Notifications

### 14. Get Notifications
**Endpoint:** `GET /api/tenant/{tenantId}/notifications`

**Description:** Retrieve payment reminders and maintenance request updates.

**Path Parameters:**
- `tenantId` (integer, required): The tenant's ID

**Success Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "paymentReminders": [
      {
        "leaseId": 1,
        "propertyName": "Marina Tower",
        "unitNumber": "A-101",
        "amountDue": 500.00,
        "dueDate": "2024-12-01T00:00:00Z",
        "daysUntilDue": 21,
        "status": "Upcoming"
      }
    ],
    "maintenanceUpdates": [
      {
        "requestId": 14,
        "category": "Plumbing",
        "priority": "High",
        "status": "In Progress",
        "description": "Leaking faucet in kitchen",
        "lastUpdate": "2024-11-09T10:00:00Z",
        "propertyName": "Marina Tower"
      }
    ],
    "unreadCount": 3
  }
}
```

---

## Error Handling

All API endpoints follow consistent error response formats:

### Common HTTP Status Codes
- **200 OK** - Request successful
- **201 Created** - Resource created successfully
- **400 Bad Request** - Invalid request data or validation error
- **401 Unauthorized** - Authentication failed
- **404 Not Found** - Resource not found
- **500 Internal Server Error** - Server error

### Error Response Format
```json
{
  "success": false,
  "message": "Detailed error message here"
}
```

### Validation Error Response
```json
{
  "success": false,
  "message": "Validation failed",
  "errors": {
    "Email": ["Email is required"],
    "Amount": ["Amount must be greater than 0"]
  }
}
```

---

## Data Models

### Tenant
```typescript
interface Tenant {
  tenantId: number;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  paciNumber: string;
  governmentId: string;
  dateOfBirth: string; // ISO 8601 format
  nationality: string;
  status: "Active" | "Inactive";
  companyId?: number;
  company?: Company;
}
```

### Lease
```typescript
interface Lease {
  leaseId: number;
  unitId: number;
  tenantId: number;
  leaseStartDate: string; // ISO 8601 format
  leaseEndDate: string; // ISO 8601 format
  monthlyRent: number;
  securityDeposit: number;
  paymentDueDay: number;
  status: "Active" | "Expired" | "Terminated";
  contractTerms?: string;
  unit?: Unit;
  tenant?: Tenant;
}
```

### Payment
```typescript
interface Payment {
  paymentId: number;
  leaseId: number;
  amountPaid: number;
  paymentDate: string; // ISO 8601 format
  paymentMethod: "Bank Transfer" | "Credit Card" | "Cash" | "Cheque" | "Online Payment";
  transactionReference?: string;
  paymentStatus: "Pending" | "Completed" | "Failed" | "Refunded";
  notes?: string;
  lease?: Lease;
}
```

### Maintenance Request
```typescript
interface MaintenanceRequest {
  requestId: number;
  propertyId: number;
  unitId?: number;
  tenantId?: number;
  category: "Plumbing" | "Electrical" | "HVAC" | "Appliance" | "Structural" | "Other";
  priority: "Low" | "Medium" | "High" | "Emergency";
  description: string;
  status: "Open" | "In Progress" | "Completed" | "Cancelled";
  assignedToUserId?: string;
  requestDate: string; // ISO 8601 format
  completedDate?: string; // ISO 8601 format
  estimatedCost?: number;
  actualCost?: number;
  notes?: string;
  property?: Property;
  unit?: Unit;
  tenant?: Tenant;
}
```

### Property
```typescript
interface Property {
  propertyId: number;
  propertyName: string;
  address: string;
  city: string;
  state?: string;
  postalCode?: string;
  propertyType: string;
  totalUnits: number;
}
```

### Unit
```typescript
interface Unit {
  unitId: number;
  propertyId: number;
  unitNumber: string;
  bedrooms?: number;
  bathrooms?: number;
  squareFeet?: number;
  rentAmount: number;
  status: "Vacant" | "Occupied" | "Maintenance";
  property?: Property;
}
```

---

## Authentication Notes

### Security Considerations
1. **Password Storage**: All passwords are hashed using BCrypt with salt rounds
2. **HTTPS**: In production, all API calls must be made over HTTPS
3. **Session Management**: After login, store the `tenantId` securely on the device
4. **Token Authentication**: Consider implementing JWT tokens for production use

### Recommended Flow for Mobile App
1. User enters Civil ID (PACI Number) and password
2. Call `POST /api/tenant/login`
3. Store `tenantId` securely (encrypted storage)
4. Use `tenantId` in all subsequent API calls
5. Implement logout to clear stored credentials

---

## Best Practices

### 1. Date Handling
- All dates are in UTC and ISO 8601 format
- Convert to local timezone in the mobile app

### 2. Amount Formatting
- All monetary amounts are in KWD (Kuwaiti Dinar)
- Format with 3 decimal places: `500.000 KWD`

### 3. Error Handling
- Always check the `success` field in responses
- Display user-friendly error messages from the `message` field
- Log full error responses for debugging

### 4. Pagination
- Use pagination for payment history to improve performance
- Default page size is 20 records
- Request smaller pages on mobile networks

### 5. Caching
- Cache dashboard data for offline viewing
- Refresh on pull-to-refresh gesture
- Show cached data with "Last updated" timestamp

### 6. Network Handling
- Implement retry logic for failed requests
- Show loading indicators during API calls
- Handle timeout scenarios gracefully

---

## Testing Credentials

For development and testing purposes:

**Test Tenant Account:**
- Civil ID (PACI Number): Check with admin using `/TenantPortal/DebugTenants`
- Default Password: `Welcome@123`

**Important:** These credentials are for development only. Production will have different credentials.

---

## Support and Contact

For API issues or questions:
- Email: dev@viijar.com
- Documentation Updates: Check this file for latest changes
- Base URL Production: TBD (To Be Determined)

---

## Changelog

### Version 1.0 (November 2024)
- Initial API release
- Authentication endpoints
- Profile management
- Lease management
- Payment submission and history
- Maintenance request management
- Dashboard and notifications

---

**Document Version:** 1.0
**Last Updated:** November 10, 2024
**API Base URL:** http://localhost:5032/api/tenant
**Production URL:** TBD
