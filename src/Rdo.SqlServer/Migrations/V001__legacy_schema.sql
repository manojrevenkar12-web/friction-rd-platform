-- The legacy desktop application's tables, as it uses them today. Created here only when absent,
-- so a development database looks like production; in production they already exist.
IF OBJECT_ID(N'dbo.TestRequest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TestRequest (
        ReqNo        INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TestRequest PRIMARY KEY,
        RequestText  NVARCHAR(MAX) NULL,
        Mix          NVARCHAR(20)  NOT NULL,
        Vehicle      NVARCHAR(50)  NOT NULL,
        Axle         CHAR(1)       NOT NULL,      -- 'F' or 'R'
        TestCode     NVARCHAR(20)  NOT NULL,
        Samples      INT           NOT NULL CONSTRAINT DF_TestRequest_Samples DEFAULT (1),
        DueDate      DATETIME      NULL,
        Status       TINYINT       NOT NULL CONSTRAINT DF_TestRequest_Status DEFAULT (0), -- 0 open, 1 running, 2 done, 9 cancelled
        CreatedBy    NVARCHAR(50)  NOT NULL,
        CreatedOn    DATETIME      NOT NULL CONSTRAINT DF_TestRequest_CreatedOn DEFAULT (GETDATE())
    );
END;
GO
IF OBJECT_ID(N'dbo.TestResult', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TestResult (
        ReqNo      INT           NOT NULL CONSTRAINT PK_TestResult PRIMARY KEY,
        MuNom      DECIMAL(4, 2) NOT NULL,
        MuFadeMin  DECIMAL(4, 2) NOT NULL,
        Passed     BIT           NOT NULL,
        EnteredOn  DATETIME      NOT NULL CONSTRAINT DF_TestResult_EnteredOn DEFAULT (GETDATE())
    );
END;
