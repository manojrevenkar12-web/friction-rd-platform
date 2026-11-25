-- The platform's own schema. Additive only: no legacy table is changed.
IF SCHEMA_ID(N'rdo') IS NULL EXEC(N'CREATE SCHEMA rdo');
GO
CREATE TABLE rdo.Request (
    Id            NVARCHAR(40)   NOT NULL CONSTRAINT PK_Request PRIMARY KEY,
    Text          NVARCHAR(4000) NOT NULL,
    SubmittedBy   NVARCHAR(100)  NOT NULL,
    SubmittedAt   DATETIMEOFFSET NOT NULL,
    Status        NVARCHAR(30)   NOT NULL,
    Model         NVARCHAR(100)  NOT NULL,
    DraftJson     NVARCHAR(MAX)  NULL,
    DecisionJson  NVARCHAR(MAX)  NOT NULL,
    OrderId       NVARCHAR(50)   NULL,
    LegacyReqNo   INT            NULL
);
GO
CREATE TABLE rdo.Application (
    Id            NVARCHAR(20)  NOT NULL CONSTRAINT PK_Application PRIMARY KEY,
    Vehicle       NVARCHAR(50)  NOT NULL,
    Axle          NVARCHAR(10)  NOT NULL,
    Fixture       NVARCHAR(20)  NOT NULL,
    VehicleClass  NVARCHAR(30)  NOT NULL
);
GO
CREATE TABLE rdo.TestOrder (
    Id           NVARCHAR(50)   NOT NULL CONSTRAINT PK_TestOrder PRIMARY KEY,
    Formulation  NVARCHAR(20)   NOT NULL,
    Application  NVARCHAR(20)   NOT NULL,
    ProcedureId  NVARCHAR(30)   NOT NULL,
    Samples      INT            NOT NULL,
    Deadline     DATETIMEOFFSET NULL,
    Priority     NVARCHAR(20)   NOT NULL,
    ReadyAt      DATETIMEOFFSET NOT NULL,
    ClosedAt     DATETIMEOFFSET NULL
);
CREATE INDEX IX_TestOrder_Open ON rdo.TestOrder (ReadyAt) WHERE ClosedAt IS NULL;
GO
CREATE TABLE rdo.Result (
    OrderId      NVARCHAR(50)   NOT NULL CONSTRAINT PK_Result PRIMARY KEY,
    Engine       NVARCHAR(20)   NOT NULL,
    ReportJson   NVARCHAR(MAX)  NOT NULL,
    ParityJson   NVARCHAR(MAX)  NULL,
    CompletedAt  DATETIMEOFFSET NOT NULL
);
GO
CREATE TABLE rdo.Audit (
    Id       BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Audit PRIMARY KEY,
    At       DATETIMEOFFSET NOT NULL,
    Actor    NVARCHAR(100)  NOT NULL,
    Action   NVARCHAR(50)   NOT NULL,
    Subject  NVARCHAR(50)   NOT NULL,
    Detail   NVARCHAR(2000) NOT NULL
);
