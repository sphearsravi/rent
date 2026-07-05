UPDATE Tenants SET Password = '$2a$11$zgMLgGXq0PO6lTPMA9UY1.0yTkFUTc1U67gj6c66035URGnLLBcB2' WHERE Status = 'Active';
SELECT TenantId, GovernmentId, Password FROM Tenants WHERE TenantId = 5;
