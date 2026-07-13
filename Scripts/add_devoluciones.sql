-- ============================================================
-- Módulo de Devoluciones
-- Ejecutar en TU_BASE_DE_DATOS
-- ============================================================

-- 1) Tabla de devoluciones
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'inventario' AND TABLE_NAME = 'devoluciones_inventario'
)
BEGIN
    CREATE TABLE inventario.devoluciones_inventario (
        id              INT IDENTITY(1,1) PRIMARY KEY,
        cod_fab         NVARCHAR(100)  NOT NULL,
        cod_int         NVARCHAR(100)  NULL,
        descripcion     NVARCHAR(500)  NOT NULL,
        cantidad        DECIMAL(18,2)  NOT NULL,
        um              NVARCHAR(50)   NULL,
        fecha_devolucion DATETIME      NOT NULL DEFAULT GETDATE(),
        motivo          NVARCHAR(500)  NULL,
        devuelve        NVARCHAR(200)  NULL,
        proyecto        NVARCHAR(200)  NULL,
        obs             NVARCHAR(500)  NULL,
        created_at      DATETIME       NOT NULL DEFAULT GETDATE()
    );
END
GO

-- 2) Stored Procedure
IF OBJECT_ID('dbo.sp_RegistrarDevolucion', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_RegistrarDevolucion;
GO

CREATE PROCEDURE dbo.sp_RegistrarDevolucion
    @cod_fab          NVARCHAR(100),
    @cod_int          NVARCHAR(100)  = NULL,
    @descripcion      NVARCHAR(500),
    @cantidad         DECIMAL(18,2),
    @um               NVARCHAR(50)   = NULL,
    @fecha_devolucion DATETIME,
    @motivo           NVARCHAR(500)  = NULL,
    @devuelve         NVARCHAR(200)  = NULL,
    @proyecto         NVARCHAR(200)  = NULL,
    @obs              NVARCHAR(500)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) Insertar en historial de devoluciones
    INSERT INTO inventario.devoluciones_inventario
        (cod_fab, cod_int, descripcion, cantidad, um, fecha_devolucion, motivo, devuelve, proyecto, obs)
    VALUES
        (LTRIM(RTRIM(@cod_fab)), @cod_int, @descripcion, @cantidad, @um,
         @fecha_devolucion, @motivo, @devuelve, @proyecto, @obs);

    -- 2) Sumar cantidad al catálogo (devolución regresa al stock)
    UPDATE inventario.catalogo_materiales
    SET cant = ISNULL(cant, 0) + @cantidad
    WHERE LTRIM(RTRIM(cod_fab)) = LTRIM(RTRIM(@cod_fab));
END
GO
