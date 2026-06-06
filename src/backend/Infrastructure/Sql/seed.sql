IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customers (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        Email NVARCHAR(150) NOT NULL,
        City NVARCHAR(100) NOT NULL,
        Status NVARCHAR(50) NOT NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Customers)
BEGIN
    INSERT INTO dbo.Customers (Name, Email, City, Status)
    VALUES
      (N'Ada Lovelace', N'ada@example.com', N'London', N'Active'),
      (N'Grace Hopper', N'grace@example.com', N'New York', N'Active'),
      (N'Alan Turing', N'alan@example.com', N'Manchester', N'Pending'),
      (N'Katherine Johnson', N'katherine@example.com', N'White Sulphur Springs', N'Active');
END;
