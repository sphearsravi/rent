# Rent Collection System

A comprehensive multi-property rent collection and management application built with .NET Core 8.0 and MySQL.

## Overview

This system enables property owners and management companies to efficiently manage multiple properties, tenants, leases, payments, and maintenance requests through a modern web interface and RESTful API for mobile integration.

## Features Implemented

### Core Functionality
- **Multi-Property Management** - Support for multiple properties under one or multiple companies
- **User Authentication & Authorization** - ASP.NET Core Identity with role-based access control
- **Dashboard** - Beautiful, responsive dashboard with key metrics and insights
- **Database** - MySQL database with Entity Framework Core

### User Roles (Configured)
1. **Super Admin** - Full system access
2. **Company Owner** - Manage company and properties
3. **Property Manager** - Property and tenant operations
4. **Accountant/Finance** - Financial operations
5. **Maintenance Staff** - Work orders and maintenance
6. **Tenant** - Self-service portal (planned)

### Database Entities
- Companies
- Users (with ASP.NET Identity)
- Properties
- Units
- Tenants
- Leases
- Payments
- Invoices
- Maintenance Requests

## Technology Stack

### Backend
- **.NET Core 8.0** - Latest LTS framework
- **C# 12** - Modern C# language features
- **Entity Framework Core 9.0** - ORM for database operations
- **MySQL 8.0+** - Relational database
- **Pomelo.EntityFrameworkCore.MySql** - MySQL provider for EF Core
- **ASP.NET Core Identity** - Authentication and authorization
- **JWT Bearer Authentication** - Token-based auth for API

### Frontend
- **ASP.NET Core MVC** - Server-side rendering
- **Razor Pages** - View engine
- **Modern CSS** - Custom responsive design with gradients and animations
- **Font Awesome 6.4** - Icon library
- **Responsive Design** - Mobile-friendly interface

## Project Structure

```
RentCollectionSystem/
├── RentCollection.API/          # RESTful API for mobile apps
├── RentCollection.Core/          # Domain entities and interfaces
│   └── Entities/                # Database models
├── RentCollection.Application/   # Business logic layer
├── RentCollection.Infrastructure/ # Data access layer
│   └── Data/                    # DbContext and repositories
├── RentCollection.Web/           # MVC web application
│   ├── Controllers/             # MVC controllers
│   ├── Views/                   # Razor views
│   └── Models/                  # View models
└── RentCollectionSystem.sln     # Solution file
```

## Architecture

### Clean Architecture Layers
1. **Core Layer** - Domain entities, no dependencies
2. **Infrastructure Layer** - EF Core DbContext, data access
3. **Application Layer** - Business logic, services
4. **Presentation Layer** - Web UI (MVC) and API

### Key Design Patterns
- **Repository Pattern** - Data access abstraction
- **Dependency Injection** - Built-in DI container
- **Entity Framework Core** - Code-first database approach
- **MVC Pattern** - Separation of concerns

## Database Schema

### Key Tables
- **Companies** - Multi-tenancy support
- **AspNetUsers** - Identity users with custom fields
- **Properties** - Property details
- **Units** - Individual rental units
- **Tenants** - Tenant information
- **Leases** - Lease agreements
- **Payments** - Payment transactions
- **Invoices** - Invoice management
- **MaintenanceRequests** - Work orders

### Relationships
- One Company → Many Properties → Many Units
- One Tenant → Many Leases → Many Payments
- One Property → Many Maintenance Requests
- Many-to-Many: Users ↔ Properties (for managers)

## Getting Started

### Prerequisites
- .NET 8.0 SDK or later
- MySQL Server 8.0 or later
- Visual Studio 2022 / VS Code / Rider

### Database Setup

1. **Install MySQL** (if not already installed)
2. **Create Database**:
   ```sql
   CREATE DATABASE RentCollectionDB;
   ```

3. **Update Connection String** in `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=localhost;Database=RentCollectionDB;User=root;Password=your_password;"
   }
   ```

### Running the Application

1. **Navigate to the solution directory**:
   ```bash
   cd RentCollectionSystem
   ```

2. **Restore NuGet packages**:
   ```bash
   dotnet restore
   ```

3. **Create database migration**:
   ```bash
   cd RentCollection.Infrastructure
   dotnet ef migrations add InitialCreate --startup-project ../RentCollection.Web
   ```

4. **Update database**:
   ```bash
   dotnet ef database update --startup-project ../RentCollection.Web
   ```

5. **Run the web application**:
   ```bash
   cd ../RentCollection.Web
   dotnet run
   ```

6. **Access the application**:
   - Open browser: `https://localhost:5001` or `http://localhost:5000`

### Running the API

```bash
cd RentCollection.API
dotnet run
```

API will be available at `https://localhost:7000` with Swagger documentation.

## Dashboard Features

### Implemented
- **Statistics Cards** - Properties, Units, Tenants, Revenue, Pending Payments, Maintenance
- **Occupancy Rate** - Visual progress bar showing unit occupancy
- **Recent Payments** - Latest payment transactions
- **Lease Expirations** - Upcoming lease end dates (30-day window)
- **Quick Actions** - Fast access to common tasks
- **Responsive Design** - Works on desktop, tablet, and mobile

### Dashboard Metrics
- Total Properties
- Occupied vs Vacant Units
- Active Tenants
- Monthly Revenue
- Pending Payments
- Open Maintenance Requests

## Configuration

### JWT Settings (appsettings.json)
```json
"Jwt": {
  "Key": "YourSuperSecretKeyMinimum32CharactersLong12345678",
  "Issuer": "RentCollectionAPI",
  "Audience": "RentCollectionClient",
  "AccessTokenExpirationMinutes": 60,
  "RefreshTokenExpirationDays": 7
}
```

### Identity Password Policy
- Minimum 8 characters
- Requires digit
- Requires uppercase letter
- Requires lowercase letter
- Requires non-alphanumeric character
- Unique email required

## API Endpoints (Planned)

### Authentication
- `POST /api/v1/auth/login` - User login
- `POST /api/v1/auth/register` - User registration
- `POST /api/v1/auth/refresh-token` - Refresh JWT token

### Properties
- `GET /api/v1/properties` - List all properties
- `GET /api/v1/properties/{id}` - Get property details
- `POST /api/v1/properties` - Create property
- `PUT /api/v1/properties/{id}` - Update property
- `DELETE /api/v1/properties/{id}` - Delete property

### Tenants
- `GET /api/v1/tenants` - List all tenants
- `POST /api/v1/tenants` - Create tenant
- ... (Full CRUD operations)

### Payments
- `GET /api/v1/payments` - List payments
- `POST /api/v1/payments` - Record payment
- ... (Additional endpoints)

## Security Features

### Implemented
- Password hashing (PBKDF2)
- Role-based authorization
- HTTPS enforcement
- SQL injection prevention (EF Core parameterized queries)
- Cookie-based authentication for web
- JWT tokens for API

### Planned
- Multi-factor authentication (MFA)
- Rate limiting
- API versioning
- CORS policy
- Audit logging

## Future Enhancements

### Short Term
1. Complete Property Management CRUD
2. Tenant Management interface
3. Lease Management
4. Payment Processing
5. Invoice Generation

### Medium Term
1. Maintenance Request System
2. Document Management
3. Reporting & Analytics
4. Email Notifications
5. Mobile API completion

### Long Term
1. Mobile Apps (iOS/Android)
2. Online Payment Integration (Stripe, PayPal)
3. Automated Rent Collection
4. Tenant Portal
5. AI-powered Analytics
6. Multi-language Support
7. E-signature Integration

## Development Guidelines

### Code Standards
- Follow C# coding conventions
- Use async/await for I/O operations
- Implement proper error handling
- Add XML documentation for public APIs
- Write unit tests (planned)

### Git Workflow
1. Create feature branch
2. Make changes
3. Test thoroughly
4. Create pull request
5. Code review
6. Merge to main

## Troubleshooting

### Common Issues

**Database Connection Error**
- Verify MySQL is running
- Check connection string credentials
- Ensure database exists

**Build Errors**
- Run `dotnet clean` then `dotnet build`
- Check NuGet package versions
- Restore packages with `dotnet restore`

**Migration Issues**
- Delete Migrations folder
- Drop database and recreate
- Run `Add-Migration InitialCreate` again

## Contributing

1. Fork the repository
2. Create feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Open Pull Request

## License

This project is licensed under the MIT License - see LICENSE file for details.

## Contact

For questions or support, please contact:
- Email: support@rentcollection.com
- Documentation: [RentCollectionSystem_Analysis.md](RentCollectionSystem_Analysis.md)

## Acknowledgments

- ASP.NET Core Team
- Entity Framework Core Team
- Pomelo Foundation (MySQL EF Core Provider)
- Font Awesome
- Community contributors

---

**Version**: 1.0.0
**Last Updated**: November 2, 2025
**Status**: MVP Complete - Ready for Extension

## Next Steps

1. **Configure MySQL Database**:
   - Update connection string with your MySQL credentials
   - Run migrations to create database schema

2. **Seed Initial Data** (Optional):
   - Create sample companies, properties, and users
   - Test the dashboard with real data

3. **Extend Functionality**:
   - Implement remaining CRUD controllers
   - Add validation and error handling
   - Create additional views for management interfaces

4. **Deploy**:
   - Configure production settings
   - Set up hosting environment
   - Deploy to IIS/Linux server

**Happy Coding!**
