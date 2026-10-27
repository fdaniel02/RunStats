-- Creates a dedicated SQL login for RunStats with rights only to its own database, for setting it up by hand.
-- scripts/setup-cluster-database.sh does the same with a generated password and seals it.
-- Run as sysadmin (e.g. sa) once the RunStats database exists. Replace <STRONG_PASSWORD> before running, and don't
-- save it back to this file.
--
-- Roles:
--   db_datareader / db_datawriter  - read and write the runs
--   db_ddladmin                    - the app applies EF Core migrations at startup
-- The backup login gets db_backupoperator, or the nightly backup job fails on this database.

USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'runstats')
    CREATE LOGIN [runstats]
        WITH PASSWORD = N'<STRONG_PASSWORD>',
             DEFAULT_DATABASE = [RunStats],
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = OFF;
GO

USE [RunStats];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'runstats')
    CREATE USER [runstats] FOR LOGIN [runstats];
GO

ALTER ROLE [db_datareader] ADD MEMBER [runstats];
ALTER ROLE [db_datawriter] ADD MEMBER [runstats];
ALTER ROLE [db_ddladmin]   ADD MEMBER [runstats];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'backup')
    CREATE USER [backup] FOR LOGIN [backup];
ALTER ROLE [db_backupoperator] ADD MEMBER [backup];
GO
