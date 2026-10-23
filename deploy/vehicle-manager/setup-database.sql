-- Vehicle Manager's database, login and database user, on the server's SQL Server.
-- Run by setup-database.sh as sa, which sets the login's password first (:setvar Password, generated on the host).
-- Safe to re-run: it changes the login's password (setup-database.sh writes the new connection string) and re-maps
-- the user to the login, e.g. after a database is imported.
SET NOCOUNT ON;
IF DB_ID(N'VehicleManager') IS NULL
BEGIN
    -- A fresh install: the app creates its tables on first start. To bring data across, import it before this.
    CREATE DATABASE [VehicleManager];
    PRINT 'Created the database VehicleManager (empty).';
END
GO
IF SUSER_ID(N'vehiclemanager') IS NULL
    CREATE LOGIN [vehiclemanager] WITH PASSWORD = N'$(Password)', DEFAULT_DATABASE = [VehicleManager],
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [vehiclemanager] WITH PASSWORD = N'$(Password)';
GO
USE [VehicleManager];
IF USER_ID(N'vehiclemanager') IS NULL
    CREATE USER [vehiclemanager] FOR LOGIN [vehiclemanager];
ELSE
    ALTER USER [vehiclemanager] WITH LOGIN = [vehiclemanager];
ALTER ROLE [db_owner] ADD MEMBER [vehiclemanager];
GO
PRINT 'Login vehiclemanager is set, and owns the database VehicleManager.';
