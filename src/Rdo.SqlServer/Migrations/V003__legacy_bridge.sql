-- Strangler bridge. While the legacy desktop application is still in use (streamed through Citrix
-- or AppStream), its reports read the legacy shape. Orders taken by the platform are shown to it
-- through this view; open legacy requests are imported into the platform once, by the procedure below.
CREATE VIEW dbo.vPlatformOrdersAsLegacy
AS
SELECT
    o.Id                          AS PlatformOrderId,
    o.Formulation                 AS Mix,
    a.Vehicle                     AS Vehicle,
    CAST(CASE a.Axle WHEN N'front' THEN 'F' ELSE 'R' END AS CHAR(1)) AS Axle,
    o.ProcedureId                 AS TestCode,
    o.Samples                     AS Samples,
    CAST(o.Deadline AS DATETIME)  AS DueDate,
    CAST(CASE WHEN o.ClosedAt IS NULL THEN 0 ELSE 2 END AS TINYINT) AS Status
FROM rdo.TestOrder AS o
JOIN rdo.Application AS a ON a.Id = o.Application;
GO
CREATE PROCEDURE rdo.ImportLegacyOpenRequests
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO rdo.TestOrder (Id, Formulation, Application, ProcedureId, Samples, Deadline, Priority, ReadyAt)
    SELECT
        CONCAT(N'LEG-', t.ReqNo),
        t.Mix,
        a.Id,
        t.TestCode,
        t.Samples,
        CAST(t.DueDate AS DATETIMEOFFSET),
        CASE WHEN t.DueDate IS NULL THEN N'exploratory' ELSE N'development' END,
        CAST(t.CreatedOn AS DATETIMEOFFSET)
    FROM dbo.TestRequest AS t
    JOIN rdo.Application AS a
        ON a.Vehicle = t.Vehicle AND a.Axle = CASE t.Axle WHEN 'F' THEN N'front' ELSE N'rear' END
    WHERE t.Status = 0
      AND NOT EXISTS (SELECT 1 FROM rdo.TestOrder AS o WHERE o.Id = CONCAT(N'LEG-', t.ReqNo));
    SELECT @@ROWCOUNT AS Imported;
END;
