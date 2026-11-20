CREATE DATABASE MEELANO_TEST;
GO
USE MEELANO_TEST;
GO
CREATE VIEW dbo.MEELANO_Checks AS
 SELECT CAST(N'T-001' AS nvarchar(50)) AS CheckNumber,
        CAST(N'incoming' AS nvarchar(30)) AS CheckKind,
        CAST(N'TEST PARTY' AS nvarchar(100)) AS PartyName,
        CAST(12345 AS decimal(18,2)) AS AmountToman,
        CAST(GETDATE() AS date) AS DueDate,
        CAST(N'pending' AS nvarchar(30)) AS CheckStatus;
GO
CREATE VIEW dbo.MEELANO_Sales AS
 SELECT CAST(GETDATE() AS date) AS SaleDate,
        CAST(N'TEST CUSTOMER' AS nvarchar(100)) AS CustomerName,
        CAST(N'TEST SELLER' AS nvarchar(100)) AS SellerName,
        CAST(12345 AS decimal(18,2)) AS AmountToman,
        CAST(123 AS decimal(18,2)) AS ProfitToman;
GO
CREATE USER meelanoci FOR LOGIN meelanoci;
GRANT SELECT ON dbo.MEELANO_Checks TO meelanoci;
GRANT SELECT ON dbo.MEELANO_Sales TO meelanoci;
GO
